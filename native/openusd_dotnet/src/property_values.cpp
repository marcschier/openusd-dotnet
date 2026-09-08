// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/property_inspection.h"

#include "pxr/base/gf/matrix2d.h"
#include "pxr/base/gf/matrix3d.h"
#include "pxr/base/gf/quatd.h"
#include "pxr/base/gf/vec2d.h"
#include "pxr/base/gf/vec2h.h"
#include "pxr/base/gf/vec4d.h"
#include "pxr/base/gf/vec4h.h"
#include "pxr/base/gf/vec4i.h"
#include "pxr/usd/ar/resolverContextBinder.h"
#include "pxr/usd/sdf/timeCode.h"

#include <charconv>
#include <type_traits>

namespace OpenUsdProperties
{
namespace
{
template <typename T>
std::string Number(T value)
{
    char buffer[128]{};
    const auto result = std::to_chars(std::begin(buffer), std::end(buffer), value);
    if (result.ec != std::errc()) throw std::runtime_error("Property numeric preview conversion failed.");
    return {buffer, result.ptr};
}

template <typename T>
std::string Format(const T& value)
{
    if constexpr (std::is_same_v<T, bool>) return value ? "true" : "false";
    else if constexpr (std::is_same_v<T, GfHalf>) return Number(static_cast<float>(value));
    else if constexpr (std::is_same_v<T, SdfTimeCode>) return Number(value.GetValue());
    else return Number(value);
}

template <typename Vector>
std::string Components(const Vector& value, size_t count)
{
    std::string text("(");
    for (size_t index = 0; index < count; ++index)
    {
        if (index) text += ", ";
        text += Format(value[index]);
    }
    return text + ')';
}

#define OPENUSD_PROPERTY_VECTOR(Type, Count) \
    template <> std::string Format(const Type& value) { return Components(value, Count); }
OPENUSD_PROPERTY_VECTOR(GfVec2d, 2)
OPENUSD_PROPERTY_VECTOR(GfVec3d, 3)
OPENUSD_PROPERTY_VECTOR(GfVec4d, 4)
OPENUSD_PROPERTY_VECTOR(GfVec2f, 2)
OPENUSD_PROPERTY_VECTOR(GfVec3f, 3)
OPENUSD_PROPERTY_VECTOR(GfVec4f, 4)
OPENUSD_PROPERTY_VECTOR(GfVec2h, 2)
OPENUSD_PROPERTY_VECTOR(GfVec3h, 3)
OPENUSD_PROPERTY_VECTOR(GfVec4h, 4)
OPENUSD_PROPERTY_VECTOR(GfVec2i, 2)
OPENUSD_PROPERTY_VECTOR(GfVec3i, 3)
OPENUSD_PROPERTY_VECTOR(GfVec4i, 4)
#undef OPENUSD_PROPERTY_VECTOR

template <typename Quaternion>
std::string QuaternionText(const Quaternion& value)
{
    const auto& imaginary = value.GetImaginary();
    return "(" + Format(value.GetReal()) + ", " + Format(imaginary[0]) +
        ", " + Format(imaginary[1]) + ", " + Format(imaginary[2]) + ")";
}

template <> std::string Format(const GfQuatd& value) { return QuaternionText(value); }
template <> std::string Format(const GfQuatf& value) { return QuaternionText(value); }
template <> std::string Format(const GfQuath& value) { return QuaternionText(value); }

template <typename Matrix>
std::string MatrixText(const Matrix& value, size_t count)
{
    std::string text("(");
    for (size_t row = 0; row < count; ++row)
    {
        if (row) text += ", ";
        text += Components(value[static_cast<int>(row)], count);
    }
    return text + ')';
}

template <> std::string Format(const GfMatrix2d& value) { return MatrixText(value, 2); }
template <> std::string Format(const GfMatrix3d& value) { return MatrixText(value, 3); }
template <> std::string Format(const GfMatrix4d& value) { return MatrixText(value, 4); }

void TextPreview(std::string_view value, openusd_property_snapshot& result,
    Budget& budget, openusd_property_preview& preview)
{
    size_t count = std::min(value.size(), static_cast<size_t>(budget.limits.maximum_preview_text_bytes));
    if (count < value.size())
    {
        while (count > 0 && (static_cast<unsigned char>(value[count]) & 0xc0) == 0x80) --count;
        preview.status = OPENUSD_PROPERTY_TRUNCATED;
        preview.reason = OPENUSD_PROPERTY_REASON_TEXT_LIMIT;
    }
    Append(result, budget, value.substr(0, count));
    ++preview.count;
}

template <typename T>
void Element(const T& value, openusd_property_snapshot& result, Budget& budget,
    openusd_property_preview& preview)
{
    if constexpr (std::is_same_v<T, std::string>) TextPreview(value, result, budget, preview);
    else if constexpr (std::is_same_v<T, TfToken>) TextPreview(value.GetString(), result, budget, preview);
    else TextPreview(Format(value), result, budget, preview);
}

template <typename T>
bool Typed(const VtValue& value, openusd_property_snapshot& result,
    Budget& budget, openusd_property_entry& entry)
{
    if (value.IsHolding<T>())
    {
        entry.value.total_count = 1;
        if (budget.limits.preview_elements) Element(value.UncheckedGet<T>(), result, budget, entry.value);
    }
    else if (value.IsHolding<VtArray<T>>())
    {
        const auto& array = value.UncheckedGet<VtArray<T>>();
        entry.value.total_count = array.size();
        const size_t count = std::min(array.size(), static_cast<size_t>(budget.limits.preview_elements));
        budget.Work(count);
        for (size_t index = 0; index < count; ++index) Element(array[index], result, budget, entry.value);
    }
    else return false;
    if (entry.value.count < entry.value.total_count && entry.value.status == OPENUSD_PROPERTY_COMPLETE)
    {
        entry.value.status = OPENUSD_PROPERTY_TRUNCATED;
        entry.value.reason = OPENUSD_PROPERTY_REASON_PREVIEW_LIMIT;
    }
    return true;
}

void Assets(const SdfAssetPath* source, size_t count, const UsdStageRefPtr& stage,
    const SdfLayerRefPtr& layer, openusd_property_snapshot& result,
    Budget& budget, openusd_property_entry& entry)
{
    entry.value.total_count = count;
    const size_t previewCount = std::min(count, static_cast<size_t>(budget.limits.preview_elements));
    budget.Work(previewCount);
    for (size_t index = 0; index < previewCount; ++index)
    {
        const auto& asset = source[index];
        const auto& authored = asset.GetAuthoredPath();
        if (!authored.empty() && authored.front() == '`')
        {
            entry.value.status = OPENUSD_PROPERTY_DEFERRED;
            entry.value.reason = OPENUSD_PROPERTY_REASON_ASSET_EXPRESSION;
            return;
        }
        for (const auto* text : {&authored, &asset.GetEvaluatedPath(), &asset.GetResolvedPath()})
        {
            if (text->size() > budget.limits.maximum_preview_text_bytes)
            {
                entry.value.status = OPENUSD_PROPERTY_TRUNCATED;
                entry.value.reason = OPENUSD_PROPERTY_REASON_TEXT_LIMIT;
                return;
            }
            budget.Text(text->size() + 1);
        }
    }
    std::vector<SdfAssetPath> assets;
    assets.reserve(previewCount);
    for (size_t index = 0; index < previewCount; ++index) assets.push_back(source[index]);
    const std::string empty;
    const auto& anchor = layer ? layer->GetIdentifier() : empty;
    if (layer && previewCount)
    {
        budget.Text(anchor.size() + layer->GetRealPath().size() + 2);
        ArResolverContextBinder binder(stage->GetPathResolverContext());
        std::vector<std::string> errors;
        // Expressions are explicitly deferred before copying their inputs.
        // These are the same native Sdf resolution and anchor semantics used
        // by UsdAttribute::Get, applied only to the admitted array prefix.
        SdfResolveAssetPaths(layer, VtDictionary(), TfSpan<SdfAssetPath>(assets), &errors);
        if (!errors.empty()) throw std::runtime_error("Native property asset resolution failed.");
    }
    for (const auto& asset : assets)
    {
        if (asset.GetAuthoredPath().size() > budget.limits.maximum_preview_text_bytes ||
            asset.GetEvaluatedPath().size() > budget.limits.maximum_preview_text_bytes ||
            asset.GetResolvedPath().size() > budget.limits.maximum_preview_text_bytes)
        {
            entry.value.status = OPENUSD_PROPERTY_TRUNCATED;
            entry.value.reason = OPENUSD_PROPERTY_REASON_TEXT_LIMIT;
            return;
        }
    }
    for (const auto& asset : assets)
    {
        TextPreview(asset.GetAuthoredPath(), result, budget, entry.value);
    }
    for (const auto& asset : assets)
    {
        const auto index = static_cast<uint32_t>(result.offsets.size());
        Append(result, budget, asset.GetAuthoredPath());
        Append(result, budget, asset.GetEvaluatedPath());
        Append(result, budget, asset.GetResolvedPath());
        Append(result, budget, anchor);
        const uint32_t missing = !layer || asset.GetAssetPath().empty() ? 2u :
            (asset.GetResolvedPath().empty() ? 1u : 0u);
        result.assets.push_back({index, missing});
        ++entry.asset_count;
    }
    if (previewCount < count)
    {
        entry.value.status = OPENUSD_PROPERTY_TRUNCATED;
        entry.value.reason = OPENUSD_PROPERTY_REASON_PREVIEW_LIMIT;
    }
}
}

bool FixedValueType(const std::type_info& type)
{
    return type == typeid(int) || type == typeid(unsigned int) || type == typeid(int64_t) ||
        type == typeid(uint64_t) || type == typeid(unsigned char) ||
        type == typeid(double) || type == typeid(float) || type == typeid(GfHalf) ||
        type == typeid(bool) || type == typeid(TfToken) || type == typeid(SdfValueBlock) ||
        type == typeid(SdfTimeCode) || type == typeid(GfVec2d) || type == typeid(GfVec3d) ||
        type == typeid(GfVec4d) || type == typeid(GfVec2f) || type == typeid(GfVec3f) ||
        type == typeid(GfVec4f) || type == typeid(GfVec2h) || type == typeid(GfVec3h) ||
        type == typeid(GfVec4h) || type == typeid(GfVec2i) || type == typeid(GfVec3i) ||
        type == typeid(GfVec4i) || type == typeid(GfMatrix2d) || type == typeid(GfMatrix3d) ||
        type == typeid(GfMatrix4d) || type == typeid(GfQuatd) || type == typeid(GfQuatf) ||
        type == typeid(GfQuath);
}

void PreviewValue(const VtValue& value, const UsdStageRefPtr& stage,
    const SdfLayerRefPtr& layer, const PcpNodeRef& node,
    openusd_property_snapshot& result, Budget& budget, openusd_property_entry& entry)
{
    entry.value.offset = static_cast<uint32_t>(result.offsets.size());
    if (value.IsEmpty()) return;
    if (value.IsHolding<SdfAssetPath>())
    {
        Assets(&value.UncheckedGet<SdfAssetPath>(), 1, stage, layer, result, budget, entry);
        return;
    }
    if (value.IsHolding<VtArray<SdfAssetPath>>())
    {
        const auto& assets = value.UncheckedGet<VtArray<SdfAssetPath>>();
        Assets(assets.cdata(), assets.size(), stage, layer, result, budget, entry);
        return;
    }
    if (value.IsHolding<VtArray<SdfTimeCode>>())
    {
        const auto& values = value.UncheckedGet<VtArray<SdfTimeCode>>();
        SdfLayerOffset offset;
        if (layer && node)
        {
            offset = node.GetMapToRoot().GetTimeOffset();
            if (const auto* local = node.GetLayerStack()->GetLayerOffsetForLayer(layer)) offset = offset * *local;
        }
        entry.value.total_count = values.size();
        const size_t count = std::min(values.size(), static_cast<size_t>(budget.limits.preview_elements));
        budget.Work(count);
        for (size_t index = 0; index < count; ++index)
            Element(SdfTimeCode(offset * values[index].GetValue()), result, budget, entry.value);
        if (count < values.size())
        {
            entry.value.status = OPENUSD_PROPERTY_TRUNCATED;
            entry.value.reason = OPENUSD_PROPERTY_REASON_PREVIEW_LIMIT;
        }
        return;
    }
#define OPENUSD_PROPERTY_TRY(Type) if (Typed<Type>(value, result, budget, entry)) return;
    OPENUSD_PROPERTY_TRY(bool)
    OPENUSD_PROPERTY_TRY(unsigned char)
    OPENUSD_PROPERTY_TRY(int)
    OPENUSD_PROPERTY_TRY(unsigned int)
    OPENUSD_PROPERTY_TRY(int64_t)
    OPENUSD_PROPERTY_TRY(uint64_t)
    OPENUSD_PROPERTY_TRY(GfHalf)
    OPENUSD_PROPERTY_TRY(float)
    OPENUSD_PROPERTY_TRY(double)
    OPENUSD_PROPERTY_TRY(SdfTimeCode)
    OPENUSD_PROPERTY_TRY(std::string)
    OPENUSD_PROPERTY_TRY(TfToken)
    OPENUSD_PROPERTY_TRY(GfVec2d)
    OPENUSD_PROPERTY_TRY(GfVec3d)
    OPENUSD_PROPERTY_TRY(GfVec4d)
    OPENUSD_PROPERTY_TRY(GfVec2f)
    OPENUSD_PROPERTY_TRY(GfVec3f)
    OPENUSD_PROPERTY_TRY(GfVec4f)
    OPENUSD_PROPERTY_TRY(GfVec2h)
    OPENUSD_PROPERTY_TRY(GfVec3h)
    OPENUSD_PROPERTY_TRY(GfVec4h)
    OPENUSD_PROPERTY_TRY(GfVec2i)
    OPENUSD_PROPERTY_TRY(GfVec3i)
    OPENUSD_PROPERTY_TRY(GfVec4i)
    OPENUSD_PROPERTY_TRY(GfMatrix2d)
    OPENUSD_PROPERTY_TRY(GfMatrix3d)
    OPENUSD_PROPERTY_TRY(GfMatrix4d)
    OPENUSD_PROPERTY_TRY(GfQuatd)
    OPENUSD_PROPERTY_TRY(GfQuatf)
    OPENUSD_PROPERTY_TRY(GfQuath)
#undef OPENUSD_PROPERTY_TRY
    Unavailable(entry.value, OPENUSD_PROPERTY_REASON_TYPE, OPENUSD_PROPERTY_UNSUPPORTED);
    entry.value.total_count = value.IsArrayValued() ? value.GetArraySize() : 1;
}
}
