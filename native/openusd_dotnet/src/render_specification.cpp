// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"
#include "internal/layer_edit.h"
#include "internal/render_array_edit.h"
#include "internal/property_composition.h"

#include "pxr/base/tf/unicodeUtils.h"
#include "pxr/base/vt/arrayEdit.h"
#include "pxr/usd/pcp/cache.h"
#include "pxr/usd/pcp/primIndex.h"
#include "pxr/usd/pcp/propertyIndex.h"
#include "pxr/usd/pcp/layerStack.h"
#include "pxr/usd/pcp/targetIndex.h"
#include "pxr/usd/sdf/data.h"
#include "pxr/usd/sdf/fileFormat.h"
#include "pxr/usd/sdf/listOp.h"
#include "pxr/usd/sdf/usdaData.h"
#include "pxr/usd/usd/tokens.h"
#include "pxr/usd/usd/primDefinition.h"
#include "pxr/usd/usd/resolver.h"
#include "pxr/usd/usdRender/spec.h"
#if defined(OPENUSD_WITH_STORAGE_ADMISSION)
#include "storage_admission_header.g.h"
#endif

#include <map>
#include <set>

struct openusd_render_specification
{
    std::vector<openusd_render_product_specification_record> products;
    std::vector<openusd_render_variable_specification_record> variables;
    std::vector<uint32_t> indices;
    std::vector<char> data;
    std::vector<size_t> offsets;
};

namespace
{
struct Budget
{
    size_t bytes = 0;
    size_t strings = 0;
    size_t settingNames = 0;
    size_t relationshipTargets = 0;
    size_t sourceVisits = 0;
    size_t materializedBytes = 0;

    void Materialization(size_t count)
    {
#if defined(OPENUSD_WITH_STORAGE_ADMISSION)
        if (auto* admission = SdfStorageAdmissionScope::GetCurrent())
        {
            admission->ReserveExternalMaterialization(count);
            materializedBytes += count;
            return;
        }
#endif
        constexpr size_t maximum = 64u << 20;
        if (count > maximum - materializedBytes)
            throw std::length_error("Render source materialization exceeds the preparation byte budget.");
        materializedBytes += count;
    }

    void Visit(size_t count = 1)
    {
        if (count > OPENUSD_RENDER_SPECIFICATION_MAX_SOURCE_VISITS - sourceVisits)
        {
            throw std::length_error("Render specification exceeds the source-admission walk budget.");
        }
#if defined(OPENUSD_WITH_STORAGE_ADMISSION)
        if (auto* admission = SdfStorageAdmissionScope::GetCurrent()) admission->_Decode(count);
#endif
        sourceVisits += count;
    }

    void String(std::string_view value)
    {
        if (value.size() >= OPENUSD_RENDER_SPECIFICATION_MAX_STRING_BYTES ||
            bytes > OPENUSD_RENDER_SPECIFICATION_MAX_STRING_BYTES - value.size() - 1 ||
            strings >= OPENUSD_RENDER_SPECIFICATION_MAX_STRINGS)
        {
            throw std::length_error("Render specification exceeds the string count or UTF-8 byte budget.");
        }
        if (value.find('\0') != std::string_view::npos)
        {
            throw std::invalid_argument("Render specification strings must not contain embedded NULs.");
        }
#if defined(OPENUSD_WITH_STORAGE_ADMISSION)
        if (auto* admission = SdfStorageAdmissionScope::GetCurrent()) admission->_Text(value.size() + 1);
#endif
        Materialization((value.size() + 1) * 4 + 64);
        TfUtf8CodePointIterator iterator(value.begin(), value.end());
        while (iterator != TfUtf8CodePointIterator::PastTheEndSentinel{})
        {
            const auto start = iterator.GetBase();
            const auto codePoint = *iterator;
            ++iterator;
            // U+FFFD itself is valid; Tf also uses it to report malformed sequences.
            if (codePoint == TfUtf8InvalidCodePoint &&
                value.substr(static_cast<size_t>(start - value.begin()),
                    static_cast<size_t>(iterator.GetBase() - start)) != "\xef\xbf\xbd")
            {
                throw std::invalid_argument("Render specification strings must be valid UTF-8.");
            }
        }
        bytes += value.size() + 1;
        ++strings;
    }

    void Path(const SdfPath& path)
    {
        if ((!path.IsPrimPath() && !path.IsPrimPropertyPath()) || path.ContainsPrimVariantSelection())
        {
            throw std::invalid_argument("Render relationship targets must be prim or prim-property paths.");
        }
        Visit(path.GetPathElementCount());
        size_t upperBytes = 1;
        for (const SdfPath& prefix : path.GetAncestorsRange())
        {
            PathElement(prefix.GetNameToken().GetString().size(), upperBytes);
        }
        EncodedPath(path, upperBytes);
    }

    template <typename TVariantSetName>
    void SourcePath(const SdfPath& path, TVariantSetName variantSetName)
    {
        if (!path.IsAbsolutePath() || !path.IsPrimOrPrimVariantSelectionPath())
        {
            throw std::invalid_argument("Render source sites must be absolute prim or variant-selection paths.");
        }
        Visit(path.GetPathElementCount());
        size_t upperBytes = 1;
        for (const SdfPath& prefix : path.GetAncestorsRange())
        {
            // GetNameToken borrows the variant selection (or set name for an empty selection).
            PathElement(prefix.GetNameToken().GetString().size(), upperBytes);
            if (prefix.IsPrimVariantSelectionPath())
            {
                PathElement(variantSetName(prefix).GetString().size(), upperBytes, 2);
            }
        }
        EncodedPath(path, upperBytes);
    }

    static void PathElement(size_t count, size_t& upperBytes, size_t syntaxBytes = 1)
    {
        if (count >= OPENUSD_RENDER_SPECIFICATION_MAX_STRING_BYTES ||
            syntaxBytes > OPENUSD_RENDER_SPECIFICATION_MAX_STRING_BYTES - count ||
            upperBytes > OPENUSD_RENDER_SPECIFICATION_MAX_STRING_BYTES - count - syntaxBytes)
        {
            throw std::length_error("Render specification path exceeds the UTF-8 byte budget.");
        }
        upperBytes += count + syntaxBytes;
    }

    void EncodedPath(const SdfPath& path, size_t upperBytes)
    {
        if (upperBytes > OPENUSD_RENDER_SPECIFICATION_MAX_STRING_BYTES - bytes)
        {
            throw std::length_error("Render specification paths exceed the UTF-8 byte budget.");
        }
        String(path.GetString());
    }
};

class RenderLayerDataAccess : public SdfFileFormat
{
public:
    RenderLayerDataAccess() = delete;

    static SdfAbstractDataConstPtr Get(const SdfLayer& layer)
    {
        return _GetLayerData(layer);
    }
};

enum class PrimRole : unsigned
{
    Forwarding = 0,
    Settings = 1,
    Product = 2,
    Variable = 4,
    Camera = 8
};

class Admission
{
public:
    explicit Admission(Budget& budget) : _budget(budget) {}

    void StageMetadata(const UsdStageRefPtr& stage)
    {
        _budget.Visit();
        const auto& index = stage->GetPseudoRoot().GetPrimIndex();
        if (!index.IsValid() || !index.GetRootNode() || !index.GetRootNode().GetLayerStack() ||
            OpenUsdProperties::HasStoredCompositionErrors(index) ||
            OpenUsdProperties::HasStoredCompositionErrors(*index.GetRootNode().GetLayerStack()))
            throw std::invalid_argument("Render stage metadata has stored layer-stack composition errors.");
        for (const SdfLayerHandle& layer : {SdfLayerHandle(stage->GetSessionLayer()),
                                           SdfLayerHandle(stage->GetRootLayer())})
        {
            VtValue value;
            const auto& data = Data(layer);
            _budget.Visit();
            const TfToken key("renderSettingsPrimPath");
            if (!data->Has(SdfPath::AbsoluteRootPath(), key, static_cast<VtValue*>(nullptr)))
            {
                continue;
            }
            RequireResident(data);
            if (data->Has(SdfPath::AbsoluteRootPath(), key, &value) &&
                value.IsHolding<std::string>())
            {
                _budget.String(value.UncheckedGet<std::string>());
            }
        }
    }

    void Prim(const UsdPrim& prim, PrimRole role = PrimRole::Forwarding)
    {
        if (!prim)
        {
            throw std::invalid_argument("A render specification refers to a missing prim.");
        }
        for (UsdPrim ancestor = prim; ancestor && !ancestor.IsPseudoRoot(); ancestor = ancestor.GetParent())
        {
            _budget.Visit();
            const auto& index = ancestor.GetPrimIndex();
            if (!index.IsValid() || OpenUsdProperties::HasStoredCompositionErrors(index))
                throw std::invalid_argument("Render input inventory has stored prim composition errors.");
            for (const auto& node : index.GetNodeRange())
            {
                _budget.Visit();
                if (!node || !node.GetLayerStack())
                    throw std::invalid_argument("Render input inventory has an invalid composition node.");
                if (node.IsCulled() || !node.CanContributeSpecs()) continue;
                if (OpenUsdProperties::HasStoredCompositionErrors(*node.GetLayerStack()))
                    throw std::invalid_argument("Render input inventory has stored layer-stack composition errors.");
            }
        }
        unsigned roles = static_cast<unsigned>(role);
        const auto admitted = _prims.find(prim.GetPath());
        const bool firstVisit = admitted == _prims.end();
        if (!firstVisit)
        {
            roles &= ~admitted->second;
            if (roles == 0)
            {
                return;
            }
        }
        _budget.Path(prim.GetPath());
        const PcpPrimIndex& index = prim.GetPrimIndex();
        if (!index.IsValid())
        {
            throw std::invalid_argument("A render specification prim has no valid composition index.");
        }
        for (const PcpNodeRef& node : index.GetNodeRange())
        {
            _budget.Visit();
            if (!node)
            {
                throw std::invalid_argument("A render specification contains an invalid composition node.");
            }
            if (!node.CanContributeSpecs() || !node.HasSpecs())
            {
                continue;
            }
            _budget.SourcePath(node.GetPath(), [&](const SdfPath& prefix)
            {
                return VariantSetName(prefix, node.GetLayerStack());
            });
            for (const SdfLayerRefPtr& layer : node.GetLayerStack()->GetLayers())
            {
                _budget.Visit();
                const auto& data = Data(layer);
                // The known crate backend can answer prim-spec existence without unpacking fields.
                if (data->GetSpecType(node.GetPath()) == SdfSpecTypeUnknown)
                {
                    continue;
                }
                RequireResident(data);
                Site(prim, data, node.GetPath(), roles, firstVisit);
            }
        }
        if (firstVisit) _budget.Materialization(256);
        _prims[prim.GetPath()] |= roles;
    }

    SdfSpecType StrongestKind(const SdfPath& property) const
    {
        const auto found = _kinds.find(property);
        return found == _kinds.end() ? SdfSpecTypeUnknown : found->second.kind;
    }

    SdfPathVector RelationshipTargets(const UsdRelationship& relationship)
    {
        Prim(relationship.GetPrim());
        const SdfPath path = relationship.GetPath();
        const SdfSpecType kind = StrongestKind(path);
        if (kind != SdfSpecTypeUnknown && kind != SdfSpecTypeRelationship)
        {
            throw std::invalid_argument("Render relationship has a non-relationship authored property opinion.");
        }
        const auto source = _kinds.find(path);
        if (source != _kinds.end())
            _budget.Materialization((1 + source->second.opinions + source->second.targets) * 512);
        const PcpPrimIndex& primIndex = relationship.GetPrim().GetPrimIndex();
        if (!_propertyIndexContext)
        {
            // In pinned USD mode this builder reads only the cache's identifier and IsUsd().
            // Reuse the stage's actual prim index, without recomposing or accessing its private cache.
            _budget.Materialization(16384);
            _propertyIndexContext = std::make_unique<PcpCache>(
                primIndex.GetRootNode().GetLayerStack()->GetIdentifier(), std::string(), true);
        }
        PcpPropertyIndex propertyIndex;
        PcpErrorVector errors;
        PcpBuildPrimPropertyIndex(path, *_propertyIndexContext, primIndex, &propertyIndex, &errors);
        if (!errors.empty())
        {
            throw std::invalid_argument("Render relationship property-index composition failed at " +
                path.GetString() + ".");
        }
        PcpTargetIndex targetIndex;
        PcpBuildTargetIndex(PcpSite(_propertyIndexContext->GetLayerStackIdentifier(), path),
            propertyIndex, SdfSpecTypeRelationship, &targetIndex, &errors);
        if (!errors.empty())
        {
            throw std::invalid_argument("Render relationship target-index composition failed at " +
                path.GetString() + ".");
        }
        // Usd also maps prototype targets into instance space. Preserve that mapping while
        // distinguishing a clean no-op opinion from errors (which Usd reports only as warnings).
        SdfPathVector targets;
        if (relationship.GetTargets(&targets) != targetIndex.hasTargetOpinions)
        {
            throw std::runtime_error("Render relationship target composition changed after admission.");
        }
        return targets;
    }

private:
    struct PropertySource
    {
        SdfSpecType kind;
        size_t opinions = 0;
        size_t targets = 0;
    };

    TfToken VariantSetName(const SdfPath& prefix, const PcpLayerStackPtr& layerStack)
    {
        const SdfPath parent = prefix.GetParentPath();
        const TfToken& selection = prefix.GetNameToken();
        for (const SdfLayerRefPtr& layer : layerStack->GetLayers())
        {
            const auto& data = Data(layer);
            _budget.Visit();
            if (!data->Has(parent, SdfChildrenKeys->VariantSetChildren, static_cast<VtValue*>(nullptr)))
            {
                continue;
            }
            RequireResident(data);
            VtValue children;
            if (!data->Has(parent, SdfChildrenKeys->VariantSetChildren, &children))
            {
                continue;
            }
            TfToken result;
            Tokens(children, [&](const TfToken& name)
            {
                // GetVariantSelection copies both strings. Match admitted, borrowed set names
                // against the path instead, using the resident source site's children.
                if (result.IsEmpty() &&
                    (parent.AppendVariantSelection(name.GetString(), selection.GetString()) == prefix ||
                     parent.AppendVariantSelection(name.GetString(), std::string()) == prefix))
                {
                    result = name;
                }
            });
            if (!result.IsEmpty())
            {
                return result;
            }
        }
        throw std::invalid_argument(
            "Render source-site variant names cannot be bounded without resident variantSetChildren metadata.");
    }

    void Site(
        const UsdPrim& prim, const SdfAbstractDataConstPtr& data, const SdfPath& site,
        unsigned roles, bool firstVisit)
    {
        VtValue children;
        _budget.Visit();
        if (data->Has(site, SdfChildrenKeys->PropertyChildren, &children))
        {
            Tokens(children, [&](const TfToken& name)
            {
                _budget.Visit();
                const SdfPath property = site.AppendProperty(name);
                const SdfSpecType kind = data->GetSpecType(property);
                _budget.Materialization(256);
                auto& source = _kinds.try_emplace(
                    prim.GetPath().AppendProperty(name), PropertySource{kind}).first->second;
                ++source.opinions;
                if (kind == SdfSpecTypeRelationship)
                {
                    if (firstVisit)
                    {
                        Targets(data, property, source);
                    }
                }
                else if (kind == SdfSpecTypeAttribute)
                {
                    Default(prim, data, property, name, roles);
                }
                else
                {
                    throw std::invalid_argument("Render property names refer to a missing or invalid property spec.");
                }
            });
        }
        if (!firstVisit)
        {
            return;
        }
        VtValue order;
        _budget.Visit();
        if (data->Has(site, SdfFieldKeys->PropertyOrder, &order))
        {
            Tokens(order, [](const TfToken&) {});
        }
        VtValue schemas;
        _budget.Visit();
        if (data->Has(site, UsdTokens->apiSchemas, &schemas))
        {
            if (!schemas.IsHolding<SdfTokenListOp>())
            {
                throw std::invalid_argument("Render prim apiSchemas must be a token list operation.");
            }
            const SdfTokenListOp& ops = schemas.UncheckedGet<SdfTokenListOp>();
            for (const TfTokenVector* names : {&ops.GetExplicitItems(), &ops.GetAddedItems(),
                &ops.GetPrependedItems(), &ops.GetAppendedItems(), &ops.GetDeletedItems(), &ops.GetOrderedItems()})
            {
                if (names->size() > OPENUSD_RENDER_SPECIFICATION_MAX_STRINGS - _budget.strings)
                {
                    throw std::length_error("Render specification exceeds the schema-token admission budget.");
                }
                for (const TfToken& name : *names)
                {
                    _budget.Visit();
                    _budget.String(name.GetString());
                }
            }
        }
    }

    static bool IsKnownCrateData(const SdfAbstractDataConstPtr& data)
    {
        static const SdfAbstractDataRefPtr identity = []
        {
            const SdfFileFormatConstPtr format = SdfFileFormat::FindById(TfToken("usdc"));
            if (!format)
            {
                throw std::runtime_error("The pinned usdc file format is unavailable for metadata-only admission.");
            }
            return format->InitData({});
        }();
        return data && identity &&
            OpenUsdEdit::DataAccess::ConcreteType(data) == OpenUsdEdit::DataAccess::ConcreteType(identity);
    }

    static void RequireResident(const SdfAbstractDataConstPtr& data)
    {
#if defined(OPENUSD_WITH_STORAGE_ADMISSION)
        if (IsKnownCrateData(data) && SdfStorageAdmissionScope::GetCurrent()) return;
#endif
        if (!IsResident(data))
        {
            throw std::invalid_argument(
                std::string("Bounded render source admission requires a known resident USD/review store, not ") +
                (data ? OpenUsdEdit::DataAccess::ConcreteType(data).name() : "a missing data store") +
                ". Deferred crate and custom backing stores require explicit preparation outside this query; "
                "they are not materialized implicitly.");
        }
    }

    static bool IsResident(const SdfAbstractDataConstPtr& data)
    {
        // This final project-owned class changes mutation bookkeeping, not SdfData's borrowed reads.
        return data && (OpenUsdEdit::DataAccess::ConcreteType(data) == typeid(SdfData) ||
            OpenUsdEdit::DataAccess::ConcreteType(data) == typeid(SdfUsdaData) ||
            OpenUsdEdit::DataAccess::ConcreteType(data) == typeid(OpenUsdEdit::CountedData));
    }

    const SdfAbstractDataConstPtr& Data(const SdfLayerHandle& layer)
    {
        _budget.Visit();
        if (!layer)
        {
            throw std::invalid_argument("Render source admission encountered an invalid layer.");
        }
        const auto found = _layers.find(layer.operator->());
        if (found != _layers.end())
        {
            return found->second;
        }
        SdfAbstractDataConstPtr data = RenderLayerDataAccess::Get(*layer);
        if (!IsResident(data) && !IsKnownCrateData(data))
        {
            RequireResident(data);
        }
        _budget.Materialization(256);
        return _layers.emplace(layer.operator->(), std::move(data)).first->second;
    }

    template <typename TAction>
    void Tokens(const VtValue& value, TAction action)
    {
        if (!value.IsHolding<TfTokenVector>())
        {
            throw std::invalid_argument("Render property children/order must be a token vector.");
        }
        const TfTokenVector& names = value.UncheckedGet<TfTokenVector>();
        if (names.size() > OPENUSD_RENDER_SPECIFICATION_MAX_STRINGS - _budget.strings)
        {
            throw std::length_error("Render specification exceeds the property-name admission budget.");
        }
        for (const TfToken& name : names)
        {
            _budget.Visit();
            _budget.String(name.GetString());
            action(name);
        }
    }

    void Targets(const SdfAbstractDataConstPtr& data, const SdfPath& property, PropertySource& source)
    {
        VtValue value;
        _budget.Visit();
        if (!data->Has(property, SdfFieldKeys->TargetPaths, &value))
        {
            return;
        }
        if (!value.IsHolding<SdfPathListOp>())
        {
            throw std::invalid_argument("Render relationship targetPaths must be a path list operation.");
        }
        const SdfPathListOp& ops = value.UncheckedGet<SdfPathListOp>();
        const SdfPathVector* lists[] = {&ops.GetExplicitItems(), &ops.GetAddedItems(),
            &ops.GetPrependedItems(), &ops.GetAppendedItems(), &ops.GetDeletedItems(), &ops.GetOrderedItems()};
        for (const SdfPathVector* paths : lists)
        {
            if (paths->size() > OPENUSD_RENDER_SPECIFICATION_MAX_RELATIONSHIP_TARGETS -
                _budget.relationshipTargets)
            {
                throw std::length_error("Render specification exceeds the relationship-target admission budget.");
            }
            _budget.relationshipTargets += paths->size();
            source.targets += paths->size();
        }
        for (const SdfPathVector* paths : lists)
        {
            for (const SdfPath& path : *paths)
            {
                _budget.Visit();
                _budget.Path(path);
            }
        }
    }

    void PreparePurpose(const UsdPrim& prim, const TfToken& name)
    {
        const SdfPath path = prim.GetPath().AppendProperty(name);
        if (_preparedPurposes.count(path) != 0) return;
        _budget.Materialization(256);
        _preparedPurposes.insert(path);
        std::vector<VtValue> edits;
        VtValue base;
        size_t words = 0;
        size_t literals = 0;
        for (Usd_Resolver resolver(&prim.GetPrimIndex()); resolver.IsValid(); resolver.NextLayer())
        {
            _budget.Visit();
            const auto& data = Data(resolver.GetLayer());
            RequireResident(data);
            VtValue value;
            if (!data->Has(resolver.GetLocalPath(name), SdfFieldKeys->Default, &value)) continue;
            if (value.IsHolding<VtArrayEdit<TfToken>>())
            {
                const auto& edit = value.UncheckedGet<VtArrayEdit<TfToken>>();
                const size_t count = OpenUsdRenderInput::TokenEditWordCount(edit);
                _budget.Visit(count + edit.GetLiterals().size());
                words += count;
                literals += edit.GetLiterals().size();
                for (const auto& literal : edit.GetLiterals()) _budget.String(literal.GetString());
                _budget.Materialization(sizeof(VtValue) * 2);
                edits.push_back(value);
            }
            else if (value.IsHolding<VtArray<TfToken>>())
            {
                base = std::move(value);
                break;
            }
            else if (value.IsHolding<SdfValueBlock>())
            {
                break;
            }
            else
            {
                throw std::invalid_argument("Render purposes require native token-array or array-edit opinions.");
            }
        }
        if (base.IsEmpty())
            prim.GetPrimDefinition().GetAttributeFallbackValue(name, &base);
        if (!base.IsHolding<VtArray<TfToken>>())
            throw std::invalid_argument("Render purposes have no concrete token-array base or fallback.");
        const auto& values = base.UncheckedGet<VtArray<TfToken>>();
        if (values.size() > OPENUSD_RENDER_SPECIFICATION_MAX_PURPOSES)
            throw std::length_error("Render array-edit base exceeds the purpose-list budget.");
        for (const auto& item : values) _budget.String(item.GetString());
        // Both validation and UsdRenderComputeSpec evaluate each purpose once.
        // Concatenation can recopy all words/literals at every contributing edit;
        // include both reads, capacity growth and the retained source edit values.
        const size_t preparationBytes = (words * sizeof(int64_t) + literals * sizeof(TfToken)) * 8;
        if (edits.size() + 1 > (64u << 20) / std::max(size_t{1}, preparationBytes))
            throw std::length_error("Render array-edit composition exceeds the preparation byte budget.");
        _budget.Materialization(preparationBytes * (edits.size() + 1));
        size_t size = values.size();
        for (auto edit = edits.rbegin(); edit != edits.rend(); ++edit)
        {
            const auto cost = OpenUsdRenderInput::BoundTokenEdit(
                edit->UncheckedGet<VtArrayEdit<TfToken>>(), size, OPENUSD_RENDER_SPECIFICATION_MAX_PURPOSES);
            _budget.Materialization(cost.copiedElements * sizeof(TfToken) * 16);
            size = cost.finalSize;
        }
    }

    void Default(
        const UsdPrim& prim, const SdfAbstractDataConstPtr& data,
        const SdfPath& property, const TfToken& name, unsigned roles)
    {
        const auto has = [roles](PrimRole role) { return (roles & static_cast<unsigned>(role)) != 0; };
        const bool base = has(PrimRole::Settings) || has(PrimRole::Product);
        const bool purposes = has(PrimRole::Settings) &&
            (name == UsdRenderTokens->includedPurposes || name == UsdRenderTokens->materialBindingPurposes);
        const bool token =
            (has(PrimRole::Variable) && (name == UsdRenderTokens->dataType || name == UsdRenderTokens->sourceType)) ||
            (has(PrimRole::Product) && (name == UsdRenderTokens->productName || name == UsdRenderTokens->productType)) ||
            (base && name == UsdRenderTokens->aspectRatioConformPolicy) ||
            (has(PrimRole::Settings) && name == UsdRenderTokens->renderingColorSpace);
        const bool scalar = (base && name == UsdRenderTokens->pixelAspectRatio) ||
            (has(PrimRole::Camera) &&
                (name == UsdGeomTokens->horizontalAperture || name == UsdGeomTokens->verticalAperture));
        const bool boolean = base &&
            (name == UsdRenderTokens->disableMotionBlur || name == UsdRenderTokens->disableDepthOfField);
        const bool resolution = base && name == UsdRenderTokens->resolution;
        const bool window = base && name == UsdRenderTokens->dataWindowNDC;
        const bool expression = has(PrimRole::Variable) && name == UsdRenderTokens->sourceName;
        if (!purposes && !token && !scalar && !boolean && !resolution && !window && !expression)
        {
            return;
        }
        VtValue value;
        _budget.Visit();
        if (!data->Has(property, SdfFieldKeys->Default, &value))
        {
            return;
        }
        if (value.IsHolding<SdfValueBlock>())
        {
            return;
        }
        if (purposes && value.IsHolding<VtArrayEdit<TfToken>>())
        {
            PreparePurpose(prim, name);
            return;
        }
        if (!((purposes && value.IsHolding<VtArray<TfToken>>()) ||
              (token && value.IsHolding<TfToken>()) ||
              (scalar && value.IsHolding<float>()) ||
              (boolean && value.IsHolding<bool>()) ||
              (resolution && value.IsHolding<GfVec2i>()) ||
              (window && value.IsHolding<GfVec4f>()) ||
              (expression && value.IsHolding<std::string>())))
        {
            throw std::invalid_argument(
                "The render source admission budget requires concrete schema-typed default opinions; "
                "array edits and other deferred value expressions must be prepared outside this query.");
        }
        if (value.IsHolding<std::string>())
        {
            _budget.String(value.UncheckedGet<std::string>());
        }
        else if (value.IsHolding<TfToken>())
        {
            _budget.String(value.UncheckedGet<TfToken>().GetString());
        }
        else if (purposes && value.IsHolding<VtArray<TfToken>>())
        {
            const auto& values = value.UncheckedGet<VtArray<TfToken>>();
            if (values.size() > OPENUSD_RENDER_SPECIFICATION_MAX_PURPOSES)
            {
                throw std::length_error("Render specification exceeds the purpose-list budget.");
            }
            for (const TfToken& purpose : values)
            {
                _budget.Visit();
                _budget.String(purpose.GetString());
            }
        }
    }

    Budget& _budget;
    std::map<const SdfLayer*, SdfAbstractDataConstPtr> _layers;
    std::map<SdfPath, unsigned> _prims;
    std::map<SdfPath, PropertySource> _kinds;
    std::set<SdfPath> _preparedPurposes;
    std::unique_ptr<PcpCache> _propertyIndexContext;
};

template <typename TValue>
TValue Read(const UsdAttribute& attribute)
{
    TValue value{};
    if (!attribute || !attribute.Get(&value, UsdTimeCode::Default()))
    {
        throw std::invalid_argument(
            "Could not read a default-time render value at " + attribute.GetPath().GetString() + ".");
    }
    return value;
}

template <typename TValue, typename TValidate>
void ValidateAttribute(const UsdAttribute& attribute, bool overridesOnly, TValidate validate)
{
    if (!overridesOnly || attribute.HasAuthoredValue())
    {
        if (!validate(Read<TValue>(attribute)))
        {
            throw std::invalid_argument("Invalid render value at " + attribute.GetPath().GetString() + ".");
        }
    }
}

TfToken RenderingColorSpace(const UsdRenderSettings& settings, const Admission& admission)
{
    const UsdAttribute attribute = settings.GetRenderingColorSpaceAttr();
    // A built-in attribute proxy can remain valid even when a relationship was authored at its path.
    const SdfSpecType kind = admission.StrongestKind(attribute.GetPath());
    if (!attribute || (kind != SdfSpecTypeUnknown && kind != SdfSpecTypeAttribute))
    {
        throw std::invalid_argument("renderingColorSpace must be a token attribute on " +
            settings.GetPath().GetString() + ".");
    }
    return attribute.HasAuthoredValueOpinion() ? Read<TfToken>(attribute) : TfToken();
}

bool Positive(float value)
{
    return std::isfinite(value) && value > 0;
}

bool ValidWindow(const GfVec4f& value)
{
    return std::isfinite(value[0]) && std::isfinite(value[1]) &&
        std::isfinite(value[2]) && std::isfinite(value[3]) &&
        std::isfinite(value[2] - value[0]) && std::isfinite(value[3] - value[1]) &&
        value[0] < value[2] && value[1] < value[3];
}

void ValidateBase(const UsdRenderSettingsBase& settings, bool overridesOnly)
{
    ValidateAttribute<GfVec2i>(settings.GetResolutionAttr(), overridesOnly,
        [](const GfVec2i& value) { return value[0] > 0 && value[1] > 0; });
    ValidateAttribute<float>(settings.GetPixelAspectRatioAttr(), overridesOnly, Positive);
    ValidateAttribute<GfVec4f>(settings.GetDataWindowNDCAttr(), overridesOnly, ValidWindow);
    ValidateAttribute<bool>(settings.GetDisableMotionBlurAttr(), overridesOnly, [](bool) { return true; });
    ValidateAttribute<bool>(settings.GetDisableDepthOfFieldAttr(), overridesOnly, [](bool) { return true; });
    ValidateAttribute<TfToken>(settings.GetAspectRatioConformPolicyAttr(), overridesOnly,
        [](const TfToken& value)
        {
            return value == UsdRenderTokens->expandAperture || value == UsdRenderTokens->cropAperture ||
                value == UsdRenderTokens->adjustApertureWidth || value == UsdRenderTokens->adjustApertureHeight ||
                value == UsdRenderTokens->adjustPixelAspectRatio;
        });
}

struct Inputs
{
    Budget budget;
    Admission admission{budget};
    std::map<SdfPath, size_t> validatedRelationshipDepths;
    SdfPathVector products;
    std::vector<SdfPathVector> orderedVars;
    std::map<SdfPath, std::vector<std::string>> settingNames;
};

size_t ValidateForwarding(
    const UsdStageRefPtr& stage, const UsdRelationship& relationship, Inputs& inputs,
    std::set<SdfPath>& active)
{
    inputs.admission.Prim(relationship.GetPrim());
    const SdfPath path = relationship.GetPath();
    const SdfSpecType kind = inputs.admission.StrongestKind(path);
    if (kind != SdfSpecTypeUnknown && kind != SdfSpecTypeRelationship)
    {
        throw std::invalid_argument("Render relationship has a non-relationship authored property opinion.");
    }
    if (active.size() >= OPENUSD_RENDER_SPECIFICATION_MAX_FORWARDING_DEPTH ||
        active.count(path) != 0)
    {
        throw std::invalid_argument("Render relationship forwarding is cyclic or exceeds its depth budget at " +
            path.GetString() + ".");
    }
    const auto cached = inputs.validatedRelationshipDepths.find(path);
    if (cached != inputs.validatedRelationshipDepths.end())
    {
        if (cached->second > OPENUSD_RENDER_SPECIFICATION_MAX_FORWARDING_DEPTH - active.size())
        {
            throw std::invalid_argument("Render relationship forwarding exceeds its depth budget through " +
                path.GetString() + ".");
        }
        return cached->second;
    }
    active.insert(path);
    size_t remainingDepth = 1;
    const SdfPathVector targets = inputs.admission.RelationshipTargets(relationship);
    if (targets.size() > OPENUSD_RENDER_SPECIFICATION_MAX_RELATIONSHIP_TARGETS)
    {
        throw std::length_error("Render specification exceeds the relationship-target budget.");
    }
    for (const SdfPath& target : targets)
    {
        inputs.budget.Path(target);
        if (target.IsPropertyPath())
        {
            inputs.admission.Prim(stage->GetPrimAtPath(target.GetPrimPath()));
            const UsdRelationship forwarded = stage->GetRelationshipAtPath(target);
            if (!forwarded)
            {
                throw std::invalid_argument("Render relationship forwards to a missing relationship at " +
                    target.GetString() + ".");
            }
            remainingDepth = std::max(remainingDepth, 1 + ValidateForwarding(stage, forwarded, inputs, active));
        }
        else if (!target.IsAbsolutePath() || !target.IsPrimPath())
        {
            throw std::invalid_argument("Render relationship has an invalid target at " + target.GetString() + ".");
        }
    }
    active.erase(path);
    inputs.validatedRelationshipDepths.emplace(path, remainingDepth);
    return remainingDepth;
}

SdfPathVector Forwarded(const UsdRelationship& relationship, Inputs& inputs)
{
    TfErrorMark mark;
    std::set<SdfPath> active;
    ValidateForwarding(relationship.GetPrim().GetStage(), relationship, inputs, active);
    SdfPathVector targets;
    if (!relationship.GetForwardedTargets(&targets) && !targets.empty())
    {
        throw std::runtime_error("Render relationship forwarding returned an incomplete target list.");
    }
    if (!mark.IsClean())
    {
        throw std::runtime_error(ConsumeErrors(mark));
    }
    return targets;
}

SdfPath CameraPath(const UsdRelationship& relationship, Inputs& inputs)
{
    const SdfPathVector targets = Forwarded(relationship, inputs);
    if (targets.size() > 1)
    {
        throw std::invalid_argument("Render camera relationship must resolve to at most one camera at " +
            relationship.GetPath().GetString() + ".");
    }
    return targets.empty() ? SdfPath() : targets.front();
}

void CollectSettingNames(
    const UsdPrim& prim, const TfTokenVector& schemaAttributes,
    const TfTokenVector& schemaRelationships, Inputs& inputs)
{
    inputs.admission.Prim(prim);
    auto& names = inputs.settingNames[prim.GetPath()];
    for (const UsdProperty& property : prim.GetAuthoredProperties())
    {
        const TfToken name = property.GetName();
        if (std::find(schemaAttributes.begin(), schemaAttributes.end(), name) == schemaAttributes.end() &&
            std::find(schemaRelationships.begin(), schemaRelationships.end(), name) == schemaRelationships.end())
        {
            if (++inputs.budget.settingNames > OPENUSD_RENDER_SPECIFICATION_MAX_SETTING_NAMES)
            {
                throw std::length_error("Render specification exceeds the unevaluated setting-name budget.");
            }
            names.push_back(name.GetString());
        }
    }
    for (const UsdRelationship& relationship : prim.GetAuthoredRelationships())
    {
        if (std::find(schemaRelationships.begin(), schemaRelationships.end(), relationship.GetName()) !=
            schemaRelationships.end())
        {
            continue;
        }
        const SdfPathVector targets = inputs.admission.RelationshipTargets(relationship);
        if (targets.size() > OPENUSD_RENDER_SPECIFICATION_MAX_RELATIONSHIP_TARGETS)
        {
            throw std::length_error("Render specification exceeds the custom relationship-target budget.");
        }
        for (const SdfPath& target : targets)
        {
            inputs.budget.Path(target);
        }
    }
}

void ValidateInputs(const UsdRenderSettings& settings, Inputs& inputs)
{
    const UsdStageRefPtr stage = settings.GetPrim().GetStage();
    ValidateBase(settings, false);
    const SdfPath baseCamera = CameraPath(settings.GetCameraRel(), inputs);
    inputs.products = Forwarded(settings.GetProductsRel(), inputs);
    if (inputs.products.size() > OPENUSD_RENDER_SPECIFICATION_MAX_PRODUCTS)
    {
        throw std::length_error("Render specification exceeds the product budget.");
    }
    CollectSettingNames(settings.GetPrim(), UsdRenderSettings::GetSchemaAttributeNames(),
        {UsdRenderTokens->camera, UsdRenderTokens->products}, inputs);
    std::set<SdfPath> variables;
    size_t indexCount = 0;
    for (const SdfPath& path : inputs.products)
    {
        const UsdPrim productPrim = stage->GetPrimAtPath(path);
        inputs.admission.Prim(productPrim, PrimRole::Product);
        const UsdRenderProduct product(productPrim);
        if (!product)
        {
            throw std::invalid_argument("Render products includes a missing or non-RenderProduct prim at " +
                path.GetString() + ".");
        }
        Read<TfToken>(product.GetProductNameAttr());
        Read<TfToken>(product.GetProductTypeAttr());
        ValidateBase(product, true);
        SdfPath cameraPath = CameraPath(product.GetCameraRel(), inputs);
        if (cameraPath.IsEmpty())
        {
            cameraPath = baseCamera;
        }
        const UsdPrim cameraPrim = stage->GetPrimAtPath(cameraPath);
        inputs.admission.Prim(cameraPrim, PrimRole::Camera);
        const UsdGeomCamera camera(cameraPrim);
        if (!camera)
        {
            throw std::invalid_argument("Render product " + path.GetString() +
                " requires a valid UsdGeomCamera at " + cameraPath.GetString() + ".");
        }
        if (!Positive(Read<float>(camera.GetHorizontalApertureAttr())) ||
            !Positive(Read<float>(camera.GetVerticalApertureAttr())))
        {
            throw std::invalid_argument("Render camera aperture must be finite and positive at " +
                cameraPath.GetString() + ".");
        }
        CollectSettingNames(product.GetPrim(), UsdRenderProduct::GetSchemaAttributeNames(),
            {UsdRenderTokens->camera, UsdRenderTokens->orderedVars}, inputs);
        SdfPathVector orderedVars = Forwarded(product.GetOrderedVarsRel(), inputs);
        if (orderedVars.size() > OPENUSD_RENDER_SPECIFICATION_MAX_INDICES - indexCount)
        {
            throw std::length_error("Render specification exceeds the ordered render-variable index budget.");
        }
        indexCount += orderedVars.size();
        for (const SdfPath& variablePath : orderedVars)
        {
            if (!variables.insert(variablePath).second)
            {
                continue;
            }
            if (variables.size() > OPENUSD_RENDER_SPECIFICATION_MAX_VARIABLES)
            {
                throw std::length_error("Render specification exceeds the unique render-variable budget.");
            }
            const UsdPrim variablePrim = stage->GetPrimAtPath(variablePath);
            inputs.admission.Prim(variablePrim, PrimRole::Variable);
            const UsdRenderVar variable(variablePrim);
            if (!variable)
            {
                throw std::invalid_argument("Render orderedVars includes a missing or non-RenderVar prim at " +
                    variablePath.GetString() + ".");
            }
            Read<TfToken>(variable.GetDataTypeAttr());
            const VtValue expression = Read<VtValue>(variable.GetSourceNameAttr());
            if (!expression.IsHolding<std::string>())
            {
                throw std::invalid_argument("Render variable sourceName must be a readable default-time string.");
            }
            Read<TfToken>(variable.GetSourceTypeAttr());
            CollectSettingNames(variable.GetPrim(), UsdRenderVar::GetSchemaAttributeNames(), {}, inputs);
        }
        inputs.orderedVars.push_back(std::move(orderedVars));
    }
    for (const UsdAttribute& attribute :
        {settings.GetIncludedPurposesAttr(), settings.GetMaterialBindingPurposesAttr()})
    {
        const auto purposes = Read<VtArray<TfToken>>(attribute);
        if (purposes.size() > OPENUSD_RENDER_SPECIFICATION_MAX_PURPOSES)
        {
            throw std::length_error("Render specification exceeds the purpose-list budget.");
        }
    }
    RenderingColorSpace(settings, inputs.admission);
    inputs.budget.Materialization(inputs.products.size() * 4096 + variables.size() * 2048 +
        indexCount * sizeof(size_t) * 2 + 4096);
}

void Append(openusd_render_specification& result, Budget& budget, std::string_view value)
{
    budget.String(value);
    result.offsets.push_back(result.data.size());
    result.data.insert(result.data.end(), value.begin(), value.end());
    result.data.push_back('\0');
}

void AppendNames(openusd_render_specification& result, Budget& budget, const std::vector<std::string>& names)
{
    for (const std::string& name : names)
    {
        Append(result, budget, name);
    }
}

void Pack(
    const UsdRenderSettings& settings, const UsdRenderSpec& spec, const Inputs& inputs,
    openusd_render_specification& result)
{
    if (spec.products.size() != inputs.products.size() ||
        spec.renderVars.size() > OPENUSD_RENDER_SPECIFICATION_MAX_VARIABLES)
    {
        throw std::runtime_error("Native render computation returned an incomplete product specification.");
    }
    Budget budget;
    Append(result, budget, settings.GetPath().GetString());
    budget.Materialization(sizeof(openusd_render_specification) +
        spec.products.size() * sizeof(openusd_render_product_specification_record) * 2 +
        spec.renderVars.size() * sizeof(openusd_render_variable_specification_record) * 2);
    Append(result, budget, RenderingColorSpace(settings, inputs.admission).GetString());
    for (const TfToken& purpose : spec.includedPurposes)
    {
        Append(result, budget, purpose.GetString());
    }
    for (const TfToken& purpose : spec.materialBindingPurposes)
    {
        Append(result, budget, purpose.GetString());
    }
    AppendNames(result, budget, inputs.settingNames.at(settings.GetPath()));
    for (size_t index = 0; index < spec.products.size(); ++index)
    {
        const auto& product = spec.products[index];
        const auto& names = inputs.settingNames.at(product.renderProductPath);
        const GfVec2f minimum = product.dataWindowNDC.GetMin();
        const GfVec2f maximum = product.dataWindowNDC.GetMax();
        if (product.renderProductPath != inputs.products[index] ||
            product.renderVarIndices.size() != inputs.orderedVars[index].size() ||
            product.resolution[0] <= 0 || product.resolution[1] <= 0 ||
            !Positive(product.pixelAspectRatio) || !Positive(product.apertureSize[0]) ||
            !Positive(product.apertureSize[1]) ||
            !ValidWindow(GfVec4f(minimum[0], minimum[1], maximum[0], maximum[1])))
        {
            throw std::runtime_error("Native render computation returned an invalid or incomplete product.");
        }
        openusd_render_product_specification_record record{};
        record.width = product.resolution[0];
        record.height = product.resolution[1];
        record.pixel_aspect_ratio = product.pixelAspectRatio;
        record.aperture_width = product.apertureSize[0];
        record.aperture_height = product.apertureSize[1];
        record.data_window_min_x = minimum[0];
        record.data_window_min_y = minimum[1];
        record.data_window_max_x = maximum[0];
        record.data_window_max_y = maximum[1];
        record.disable_motion_blur = product.disableMotionBlur ? 1 : 0;
        record.disable_depth_of_field = product.disableDepthOfField ? 1 : 0;
        record.string_offset = result.offsets.size();
        record.namespaced_setting_count = names.size();
        record.render_var_index_offset = result.indices.size();
        record.render_var_index_count = product.renderVarIndices.size();
        budget.Materialization(product.renderVarIndices.size() * sizeof(uint32_t) * 2);
        Append(result, budget, product.renderProductPath.GetString());
        Append(result, budget, product.name.GetString());
        Append(result, budget, product.type.GetString());
        Append(result, budget, product.cameraPath.GetString());
        Append(result, budget, product.aspectRatioConformPolicy.GetString());
        AppendNames(result, budget, names);
        for (size_t variableIndex = 0; variableIndex < product.renderVarIndices.size(); ++variableIndex)
        {
            const size_t variable = product.renderVarIndices[variableIndex];
            if (variable >= spec.renderVars.size() ||
                spec.renderVars[variable].renderVarPath != inputs.orderedVars[index][variableIndex])
            {
                throw std::runtime_error("Native render computation changed or omitted an ordered render variable.");
            }
            result.indices.push_back(static_cast<uint32_t>(variable));
        }
        result.products.push_back(record);
    }
    for (const auto& variable : spec.renderVars)
    {
        const auto& names = inputs.settingNames.at(variable.renderVarPath);
        result.variables.push_back({result.offsets.size(), names.size()});
        Append(result, budget, variable.renderVarPath.GetString());
        Append(result, budget, variable.dataType.GetString());
        Append(result, budget, variable.sourceName);
        Append(result, budget, variable.sourceType.GetString());
        AppendNames(result, budget, names);
    }
}
}

openusd_status openusd_render_get_specification(
    const openusd_stage* stage,
    const char* settings_path,
    openusd_render_specification** specification,
    openusd_render_specification_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        uint32_t size = 0;
        uint32_t version = 0;
        if (view != nullptr)
        {
            std::memcpy(&size, view, sizeof(size));
            if (size >= sizeof(size) + sizeof(version))
            {
                std::memcpy(&version, reinterpret_cast<const char*>(view) + sizeof(size), sizeof(version));
            }
        }
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(specification);
        ResetVersionedAbiOutput(view);
        if (stage == nullptr || !stage->value || specification == nullptr ||
            !IsAligned(specification) || view == nullptr || !IsAligned(view) ||
            size < sizeof(*view) || version != OPENUSD_RENDER_SPECIFICATION_VIEW_VERSION)
        {
            WriteError(error, "A valid stage, aligned owner output, and render specification view version 1 are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        view->version = OPENUSD_RENDER_SPECIFICATION_VIEW_VERSION;
#if defined(OPENUSD_WITH_STORAGE_ADMISSION)
        if (SdfStorageAdmissionScope::GetApiVersion() != 1)
            throw std::runtime_error("A matching SDK storage-admission accessor version 1 is required.");
        SdfStorageAdmissionScope storageAdmission(SdfStorageAdmissionLimits{});
#endif
        TfErrorMark mark;
        Inputs inputs;
        std::string path;
        if (settings_path == nullptr)
        {
            inputs.admission.StageMetadata(stage->value);
            const TfToken key("renderSettingsPrimPath");
            if (!stage->value->HasAuthoredMetadata(key))
            {
#if defined(OPENUSD_WITH_STORAGE_ADMISSION)
                storageAdmission.RequireSucceeded();
#endif
                return OPENUSD_STATUS_OK;
            }
            VtValue value;
            if (!stage->value->GetMetadata(key, &value) || !value.IsHolding<std::string>())
            {
                WriteError(error, "Authored renderSettingsPrimPath must be a readable absolute prim path.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            path = value.UncheckedGet<std::string>();
        }
        else
        {
            size_t length = 0;
            while (length < OPENUSD_RENDER_SPECIFICATION_MAX_STRING_BYTES && settings_path[length] != '\0')
            {
                ++length;
            }
            if (length == OPENUSD_RENDER_SPECIFICATION_MAX_STRING_BYTES)
            {
                throw std::length_error("Render settings path exceeds the UTF-8 byte budget before copying.");
            }
            inputs.budget.String(std::string_view(settings_path, length));
            path.assign(settings_path, length);
        }
        Budget pathBudget;
        pathBudget.String(path);
        if (path.empty() || !IsValidPrimPath(path.c_str()))
        {
            WriteError(error, "The explicit or authored default render settings path must be an absolute prim path.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdPrim settingsPrim = stage->value->GetPrimAtPath(SdfPath(path));
        if (!settingsPrim)
        {
            WriteError(error, "No valid UsdRenderSettings prim exists at " + path + ".");
            return OPENUSD_STATUS_NOT_FOUND;
        }
        inputs.admission.Prim(settingsPrim, PrimRole::Settings);
        const UsdRenderSettings settings(settingsPrim);
        if (!settings)
        {
            WriteError(error, "No valid UsdRenderSettings prim exists at " + path + ".");
            return OPENUSD_STATUS_NOT_FOUND;
        }
        ValidateInputs(settings, inputs);
        // A nonempty filter containing the empty namespace requests no renderer-specific
        // attribute values. We preserve their names separately, without fetching arbitrary Vt values.
        const UsdRenderSpec spec = UsdRenderComputeSpec(settings, TfTokenVector{TfToken()});
        auto result = std::make_unique<openusd_render_specification>();
        Pack(settings, spec, inputs, *result);
#if defined(OPENUSD_WITH_STORAGE_ADMISSION)
        storageAdmission.RequireSucceeded();
#endif
        if (!mark.IsClean())
        {
            WriteError(error, ConsumeErrors(mark));
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        view->has_settings = 1;
        view->products = result->products.empty() ? nullptr : result->products.data();
        view->products_size = result->products.size() * sizeof(openusd_render_product_specification_record);
        view->product_count = result->products.size();
        view->variables = result->variables.empty() ? nullptr : result->variables.data();
        view->variables_size = result->variables.size() * sizeof(openusd_render_variable_specification_record);
        view->variable_count = result->variables.size();
        view->render_var_indices = result->indices.empty() ? nullptr : result->indices.data();
        view->render_var_indices_size = result->indices.size() * sizeof(uint32_t);
        view->render_var_index_count = result->indices.size();
        view->data = result->data.data();
        view->data_size = result->data.size();
        view->offsets = result->offsets.data();
        view->offsets_size = result->offsets.size() * sizeof(size_t);
        view->string_count = result->offsets.size();
        view->included_purpose_count = spec.includedPurposes.size();
        view->material_binding_purpose_count = spec.materialBindingPurposes.size();
        view->namespaced_setting_count = inputs.settingNames.at(settings.GetPath()).size();
        *specification = result.release();
        return OPENUSD_STATUS_OK;
    });
}

void openusd_render_specification_release(openusd_render_specification* specification)
{
    delete specification;
}
