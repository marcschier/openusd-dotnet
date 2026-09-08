// Copyright (c) marcschier. Licensed under the MIT License.
#include "internal/review_document.h"
#include "internal/review_inspection_schema.h"
#include "pxr/usd/sdf/pathExpression.h"
#include "pxr/usd/sdf/schemaTypeRegistration.h"

#include <initializer_list>
#include <typeindex>
#include <type_traits>

namespace OpenUsdReview
{
namespace
{
// Field applicability and whole-value validators follow sdf/schema.cpp at
// OpenUSD 26.05, commit 2095fafafd033fa23386d7ec6d58c7cc33974518.
// Unlike SdfSchema, these immutable rules never register/discover plugin fields.
static_assert(PXR_VERSION == 2605, "Revalidate the private inspection rules when upgrading OpenUSD.");
static_assert(SdfNumSpecTypes <= 32, "Inspection spec masks require at most 32 spec kinds.");

constexpr uint32_t SpecBit(SdfSpecType type)
{
    return uint32_t{1} << static_cast<uint32_t>(type);
}

class InspectionRules
{
public:
    InspectionRules()
    {
        SdfRegisterFields(this);
        Allow(SpecBit(SdfSpecTypePseudoRoot), {
            SdfFieldKeys->ColorConfiguration, SdfFieldKeys->ColorManagementSystem,
            SdfFieldKeys->Comment, SdfFieldKeys->CustomLayerData, SdfFieldKeys->DefaultPrim,
            SdfFieldKeys->Documentation, SdfFieldKeys->EndTimeCode, SdfFieldKeys->ExpressionVariables,
            SdfFieldKeys->FramesPerSecond, SdfFieldKeys->FramePrecision, SdfFieldKeys->HasOwnedSubLayers,
            SdfFieldKeys->Owner, SdfFieldKeys->SessionOwner, SdfFieldKeys->StartTimeCode,
            SdfFieldKeys->TimeCodesPerSecond, SdfFieldKeys->EndFrame, SdfFieldKeys->StartFrame,
            SdfChildrenKeys->PrimChildren, SdfFieldKeys->LayerRelocates, SdfFieldKeys->PrimOrder,
            SdfFieldKeys->SubLayers, SdfFieldKeys->SubLayerOffsets});
        Allow(SpecBit(SdfSpecTypePrim) | SpecBit(SdfSpecTypeVariant), {
            SdfFieldKeys->Specifier, SdfFieldKeys->Comment, SdfFieldKeys->InheritPaths,
            SdfFieldKeys->Specializes, SdfChildrenKeys->PrimChildren, SdfFieldKeys->PrimOrder,
            SdfChildrenKeys->PropertyChildren, SdfFieldKeys->PropertyOrder, SdfFieldKeys->References,
            SdfFieldKeys->Relocates, SdfFieldKeys->VariantSelection, SdfChildrenKeys->VariantSetChildren,
            SdfFieldKeys->VariantSetNames, SdfFieldKeys->Active, SdfFieldKeys->AssetInfo,
            SdfFieldKeys->Clips, SdfFieldKeys->ClipSets, SdfFieldKeys->CustomData,
            SdfFieldKeys->DisplayGroupOrder, SdfFieldKeys->DisplayName, SdfFieldKeys->Documentation,
            SdfFieldKeys->Hidden, SdfFieldKeys->Instanceable, SdfFieldKeys->Kind, SdfFieldKeys->Payload,
            SdfFieldKeys->Permission, SdfFieldKeys->Prefix, SdfFieldKeys->PrefixSubstitutions,
            SdfFieldKeys->Suffix, SdfFieldKeys->SuffixSubstitutions, SdfFieldKeys->SymmetricPeer,
            SdfFieldKeys->SymmetryArguments, SdfFieldKeys->SymmetryFunction, SdfFieldKeys->TypeName});
        Allow(SpecBit(SdfSpecTypeAttribute) | SpecBit(SdfSpecTypeRelationship), {
            SdfFieldKeys->Custom, SdfFieldKeys->Variability, SdfFieldKeys->Comment, SdfFieldKeys->Default,
            SdfFieldKeys->AssetInfo, SdfFieldKeys->CustomData, SdfFieldKeys->DisplayGroup,
            SdfFieldKeys->DisplayName, SdfFieldKeys->Documentation, SdfFieldKeys->Hidden,
            SdfFieldKeys->Permission, SdfFieldKeys->Prefix, SdfFieldKeys->Suffix,
            SdfFieldKeys->SymmetricPeer, SdfFieldKeys->SymmetryArguments, SdfFieldKeys->SymmetryFunction});
        Allow(SpecBit(SdfSpecTypeAttribute), {
            SdfFieldKeys->TypeName, SdfFieldKeys->Spline, SdfChildrenKeys->ConnectionChildren,
            SdfFieldKeys->ConnectionPaths, SdfFieldKeys->DisplayUnit, SdfFieldKeys->TimeSamples,
            SdfFieldKeys->AllowedTokens, SdfFieldKeys->ArraySizeConstraint, SdfFieldKeys->ColorSpace,
            SdfFieldKeys->Limits});
        Allow(SpecBit(SdfSpecTypeRelationship), {
            SdfChildrenKeys->RelationshipTargetChildren, SdfFieldKeys->TargetPaths, SdfFieldKeys->NoLoadHint});
        Allow(SpecBit(SdfSpecTypeVariantSet), {SdfChildrenKeys->VariantChildren});

        // Derive concrete types and canonical names from the SDK without
        // initializing either SdfSchema or the process-wide TfType registry.
#define OPENUSD_REVIEW_VALUE_TYPE(unused, entry) \
        AddType<SDF_VALUE_CPP_TYPE(entry)>(TF_PP_STRINGIZE(TF_PP_TUPLE_ELEM(1, entry)));
        TF_PP_SEQ_FOR_EACH(OPENUSD_REVIEW_VALUE_TYPE, ~, SDF_VALUE_TYPES)
#undef OPENUSD_REVIEW_VALUE_TYPE
        const std::pair<const char*, const char*> components[] = {{"h", "half"}, {"f", "float"}, {"d", "double"}};
        for (const auto& component : components)
        {
            for (const char* role : {"point", "vector", "normal", "color"})
            {
                AddAlias(std::string(role) + "3" + component.first, std::string(component.second) + "3");
            }
            AddAlias(std::string("color4") + component.first, std::string(component.second) + "4");
            AddAlias(std::string("texCoord2") + component.first, std::string(component.second) + "2");
            AddAlias(std::string("texCoord3") + component.first, std::string(component.second) + "3");
        }
        AddAlias("frame4d", "matrix4d");
        AddAlias("group", "opaque", false);
        // Pinned legacy names/roles, including the role-only Transform and
        // index types. Declaration checks compare C++ types, not role identity.
        const std::pair<const char*, const char*> legacy[] = {
            {"Vec2i", "int2"}, {"Vec2h", "half2"}, {"Vec2f", "float2"}, {"Vec2d", "double2"},
            {"Vec3i", "int3"}, {"Vec3h", "half3"}, {"Vec3f", "float3"}, {"Vec3d", "double3"},
            {"Vec4i", "int4"}, {"Vec4h", "half4"}, {"Vec4f", "float4"}, {"Vec4d", "double4"},
            {"Point", "double3"}, {"PointFloat", "float3"}, {"Normal", "double3"}, {"NormalFloat", "float3"},
            {"Vector", "double3"}, {"VectorFloat", "float3"}, {"Color", "double3"}, {"ColorFloat", "float3"},
            {"Quath", "quath"}, {"Quatf", "quatf"}, {"Quatd", "quatd"},
            {"Matrix2d", "matrix2d"}, {"Matrix3d", "matrix3d"}, {"Matrix4d", "matrix4d"},
            {"Frame", "matrix4d"}, {"Transform", "matrix4d"},
            {"PointIndex", "int"}, {"EdgeIndex", "int"}, {"FaceIndex", "int"}};
        for (const auto& alias : legacy) { AddAlias(alias.first, alias.second); }
    }

    template <class T> void RegisterField(const TfToken& field)
    {
        FieldRule rule;
        if constexpr (!std::is_same_v<T, VtValue>) { rule.type = &typeid(T); }
        fields.emplace(field, rule);
    }

    void ValidateField(const TfToken& field, const VtValue& value, SdfSpecType type) const
    {
        const auto found = fields.find(field);
        // Unknown metadata remains codec-bounded typed data. Normal read/import
        // performs any plugin field validation later through the real schema.
        if (found == fields.end()) { return; }
        const auto& rule = found->second;
        Check(static_cast<uint32_t>(type) < static_cast<uint32_t>(SdfNumSpecTypes)
            && (rule.specs & SpecBit(type)) != 0, "Portable field is invalid for its spec kind.");
        Check(!rule.type || *rule.type == value.GetTypeid(),
            "Portable field concrete type differs from its native schema.");
        // FieldDefinition::IsValidValue does not invoke list/map validators.
        // These are the only two whole-value validators in the pinned schema.
        if (field == SdfFieldKeys->Default)
        {
            Check(value.IsHolding<SdfValueBlock>() || IsSceneValue(value),
                "Portable field value is invalid for the native schema.");
        }
        else if (field == SdfFieldKeys->FramesPerSecond)
        {
            Check(value.UncheckedGet<double>() > 0.0, "Portable field value is invalid for the native schema.");
        }
    }

    const std::type_info* FindType(const TfToken& name) const
    {
        const auto found = types.find(name);
        return found == types.end() ? nullptr : found->second;
    }

private:
    struct FieldRule
    {
        const std::type_info* type = nullptr;
        uint32_t specs = 0;
    };
    std::map<TfToken, FieldRule> fields;
    std::map<TfToken, const std::type_info*> types;
    std::set<std::type_index> sceneTypes;

    void Allow(uint32_t specs, std::initializer_list<TfToken> names)
    {
        for (const auto& name : names) { fields.at(name).specs |= specs; }
    }

    template <class T> void AddType(const char* name)
    {
        types.emplace(TfToken(name), &typeid(T));
        sceneTypes.insert(std::type_index(typeid(T)));
        // The pinned registry registers opaque/group with NoArrays().
        if constexpr (!std::is_same_v<T, SdfOpaqueValue>)
        {
            types.emplace(TfToken(std::string(name) + "[]"), &typeid(VtArray<T>));
            sceneTypes.insert(std::type_index(typeid(VtArray<T>)));
        }
    }

    void AddAlias(const std::string& name, const std::string& type, bool arrays = true)
    {
        types.emplace(TfToken(name), types.at(TfToken(type)));
        if (arrays) { types.emplace(TfToken(name + "[]"), types.at(TfToken(type + "[]"))); }
    }

    bool IsSceneValue(const VtValue& value, size_t depth = 0) const
    {
        if (depth >= 16) { return false; }
        if (value.IsEmpty()) { return true; }
        if (value.IsHolding<VtDictionary>())
        {
            for (const auto& item : value.UncheckedGet<VtDictionary>())
            {
                if (!IsSceneValue(item.second, depth + 1)) { return false; }
            }
            return true;
        }
        if (value.IsHolding<SdfPathExpression>()) { return value.UncheckedGet<SdfPathExpression>().IsAbsolute(); }
        return sceneTypes.count(std::type_index(value.GetTypeid())) != 0;
    }
};

const InspectionRules& Rules()
{
    static const InspectionRules rules;
    return rules;
}
}

void ValidateInspectionField(const TfToken& field, const VtValue& value, SdfSpecType type)
{
    Rules().ValidateField(field, value, type);
}

const std::type_info* InspectionValueType(const TfToken& name)
{
    return Rules().FindType(name);
}
}
