// Copyright (c) marcschier. Licensed under the MIT License.
#include "internal/review_document.h"
#include "pxr/base/gf/matrix2d.h"
#include "pxr/base/gf/matrix3d.h"
#include "pxr/base/gf/quatd.h"
#include "pxr/base/gf/vec2d.h"
#include "pxr/base/gf/vec2h.h"
#include "pxr/base/gf/vec3i.h"
#include "pxr/base/gf/vec4d.h"
#include "pxr/base/gf/vec4h.h"
#include "pxr/base/gf/vec4i.h"
#include "pxr/usd/sdf/timeCode.h"

namespace OpenUsdEdit
{
void AdmitPortablePathText(std::string_view text)
{
    // Count nested target components too, before the SDK parses/interns paths.
    size_t elements = 0;
    bool tokenStart = true;
    bool variant = false;
    for (size_t i = 0; i < text.size(); ++i)
    {
        const char ch = text[i];
        if (variant)
        {
            if (ch == '}') { variant = false; tokenStart = true; }
            continue;
        }
        if (ch == '/') { tokenStart = true; continue; }
        if (ch == '{')
        {
            ++elements; variant = true; tokenStart = false;
        }
        else if (ch == '[')
        {
            ++elements; tokenStart = true;
        }
        else if (ch == ']') { tokenStart = false; }
        else if (ch == '.')
        {
            if (tokenStart && i + 1 < text.size() && text[i + 1] == '.')
            {
                ++elements; ++i; tokenStart = false;
            }
            else { tokenStart = true; }
        }
        else if (tokenStart)
        {
            ++elements; tokenStart = false;
        }
        Check(elements <= MaxDepth, "Portable value path exceeds 32 elements before materialization.");
    }
}

namespace
{
void PathValue(Writer& writer, const SdfPath& path)
{
    Check(path.GetPathElementCount() <= MaxDepth, "Portable value path exceeds 32 elements.");
    AdmitPortablePathText(path.GetString());
    writer.Text(path.GetString());
}
SdfPath PathValue(Reader& reader)
{
    const auto text = reader.Text();
    AdmitPortablePathText(text);
    Check(text.empty() || SdfPath::IsValidPathString(text), "Invalid portable value path.");
    SdfPath path(text);
    Check(path.GetPathElementCount() <= MaxDepth, "Portable value path exceeds 32 elements.");
    return path;
}
void Offset(Writer& writer, const SdfLayerOffset& offset)
{
    Check(offset.IsValid(), "Invalid portable layer offset.");
    writer.F64(offset.GetOffset()); writer.F64(offset.GetScale());
}
SdfLayerOffset Offset(Reader& reader)
{
    const double offset = reader.F64(), scale = reader.F64();
    SdfLayerOffset result(offset, scale);
    Check(result.IsValid(), "Invalid portable layer offset.");
    return result;
}
void Arc(Writer& w, const SdfReference& value, size_t depth)
{
    w.Text(value.GetAssetPath()); PathValue(w, value.GetPrimPath()); Offset(w, value.GetLayerOffset());
    EncodeValue(w, VtValue(value.GetCustomData()), depth + 1);
}
void Arc(Writer& w, const SdfPayload& value, size_t)
{
    w.Text(value.GetAssetPath()); PathValue(w, value.GetPrimPath()); Offset(w, value.GetLayerOffset());
}
template <class T> T Arc(Reader& r, size_t depth)
{
    const auto asset = r.Text();
    const auto path = PathValue(r);
    Check(path.IsEmpty() || (path.IsAbsolutePath() && path.IsPrimPath() && !path.ContainsPrimVariantSelection()),
        "A portable reference/payload prim path must be empty or an absolute non-variant prim path.");
    const auto offset = Offset(r);
    if constexpr (std::is_same_v<T, SdfReference>)
    {
        const auto dictionary = DecodeValue(r, depth + 1);
        Check(dictionary.IsHolding<VtDictionary>(), "Reference customData must be a dictionary.");
        return T(asset, path, offset, dictionary.UncheckedGet<VtDictionary>());
    }
    else { return T(asset, path, offset); }
}
template <class T> void ListItem(Writer& w, const T& value, size_t depth)
{
    if constexpr (std::is_same_v<T, SdfReference> || std::is_same_v<T, SdfPayload>) { Arc(w, value, depth); }
    else { EncodeValue(w, VtValue(value), depth + 1); }
}
template <class T> T ListItem(Reader& r, size_t depth)
{
    if constexpr (std::is_same_v<T, SdfReference> || std::is_same_v<T, SdfPayload>) { return Arc<T>(r, depth); }
    else
    {
        auto value = DecodeValue(r, depth + 1);
        Check(value.IsHolding<T>(), "Portable list item concrete type mismatch.");
        return value.UncheckedGet<T>();
    }
}
template <class T> void List(Writer& w, const SdfListOp<T>& value, size_t depth)
{
    w.U32(value.IsExplicit() ? 1 : 0);
    size_t total = 0;
    for (const auto* items : {&value.GetExplicitItems(), &value.GetAddedItems(), &value.GetPrependedItems(),
        &value.GetAppendedItems(), &value.GetDeletedItems(), &value.GetOrderedItems()})
    {
        total += items->size();
        Check(total <= MaxItems, "Portable list-op exceeds 4096 items across all buckets.");
        w.U32(static_cast<uint32_t>(items->size()));
        for (const auto& item : *items) { ListItem(w, item, depth); }
    }
}
template <class T> VtValue List(Reader& r, size_t depth)
{
    const bool explicitMode = r.Count(1) != 0;
    std::vector<T> buckets[6];
    size_t total = 0;
    for (auto& items : buckets)
    {
        const auto count = r.Count(); total += count;
        Check(total <= MaxItems, "Portable list-op exceeds 4096 items across all buckets.");
        r.Need(static_cast<size_t>(count) * 4);
        items.reserve(count);
        for (uint32_t i = 0; i < count; ++i) { items.push_back(ListItem<T>(r, depth)); }
        Check(std::set<T>(items.begin(), items.end()).size() == items.size(), "Duplicate portable list-op bucket item.");
    }
    SdfListOp<T> result;
    if (explicitMode)
    {
        for (size_t i = 1; i < 6; ++i) { Check(buckets[i].empty(), "Mixed explicit/non-explicit portable list operation."); }
        result.SetExplicitItems(buckets[0]);
    }
    else
    {
        Check(buckets[0].empty(), "Non-explicit portable list has explicit items.");
        result.SetAddedItems(buckets[1]); result.SetPrependedItems(buckets[2]); result.SetAppendedItems(buckets[3]);
        result.SetDeletedItems(buckets[4]); result.SetOrderedItems(buckets[5]);
    }
    return VtValue(result);
}

template <class T> void Number(Writer& w, T value)
{
    if constexpr (std::is_same_v<T, GfHalf>) { w.U32(value.bits()); }
    else if constexpr (std::is_same_v<T, double>) { w.F64(value); }
    else if constexpr (std::is_same_v<T, float>) { w.F32(value); }
    else { w.U32(static_cast<uint32_t>(value)); }
}
template <class T> T Number(Reader& r)
{
    if constexpr (std::is_same_v<T, GfHalf>)
    {
        GfHalf value; value.setBits(static_cast<uint16_t>(r.Count(65535))); return value;
    }
    else if constexpr (std::is_same_v<T, double>) { return r.F64(); }
    else if constexpr (std::is_same_v<T, float>) { return r.F32(); }
    else { return static_cast<T>(r.U32()); }
}
template <class T, size_t N> void Vector(Writer& w, const T& v)
{
    for (size_t i = 0; i < N; ++i) { Number(w, v[static_cast<int>(i)]); }
}
template <class T, size_t N> VtValue Vector(Reader& r)
{
    T value;
    for (size_t i = 0; i < N; ++i) { value[static_cast<int>(i)] = Number<typename T::ScalarType>(r); }
    return VtValue(value);
}
template <class T, size_t N> void Matrix(Writer& w, const T& v)
{
    for (size_t row = 0; row < N; ++row)
    {
        for (size_t column = 0; column < N; ++column) { w.F64(v[static_cast<int>(row)][static_cast<int>(column)]); }
    }
}
template <class T, size_t N> VtValue Matrix(Reader& r)
{
    T value;
    for (size_t row = 0; row < N; ++row)
    {
        for (size_t column = 0; column < N; ++column) { value[static_cast<int>(row)][static_cast<int>(column)] = r.F64(); }
    }
    return VtValue(value);
}
template <class T> void Quaternion(Writer& w, const T& value)
{
    Number(w, value.GetReal());
    for (size_t i = 0; i < 3; ++i) { Number(w, value.GetImaginary()[static_cast<int>(i)]); }
}
template <class T> VtValue Quaternion(Reader& r)
{
    const auto real = Number<typename T::ScalarType>(r);
    typename T::ImaginaryType imaginary;
    for (size_t i = 0; i < 3; ++i) { imaginary[static_cast<int>(i)] = Number<typename T::ScalarType>(r); }
    return VtValue(T(real, imaginary));
}
template <class T> bool Array(Writer& w, const VtValue& value, uint32_t tag, size_t depth)
{
    if (!value.IsHolding<VtArray<T>>()) { return false; }
    const auto& array = value.UncheckedGet<VtArray<T>>();
    Check(array.size() <= MaxItems, "Portable array element budget exceeded.");
    w.U32(256 + tag); w.U32(static_cast<uint32_t>(array.size()));
    for (const auto& item : array) { EncodeValue(w, VtValue(item), depth + 1); }
    return true;
}
template <class T> VtValue Array(Reader& r, size_t depth)
{
    const auto count = r.Count(); r.Need(static_cast<size_t>(count) * 4);
    VtArray<T> array(count);
    for (auto& item : array)
    {
        auto value = DecodeValue(r, depth + 1);
        Check(value.IsHolding<T>(), "Portable array concrete element type mismatch.");
        item = value.UncheckedGet<T>();
    }
    return VtValue(std::move(array));
}
}

bool EncodePortableValue(Writer& w, const VtValue& v, size_t depth)
{
    if (v.IsHolding<SdfReferenceListOp>()) { w.U32(32); List(w, v.UncheckedGet<SdfReferenceListOp>(), depth); }
    else if (v.IsHolding<SdfPayloadListOp>()) { w.U32(33); List(w, v.UncheckedGet<SdfPayloadListOp>(), depth); }
    else if (v.IsHolding<std::vector<SdfLayerOffset>>())
    {
        const auto& offsets = v.UncheckedGet<std::vector<SdfLayerOffset>>();
        Check(offsets.size() <= MaxItems, "Portable sublayer-offset budget exceeded.");
        w.U32(34); w.U32(static_cast<uint32_t>(offsets.size()));
        for (const auto& offset : offsets) { Offset(w, offset); }
    }
    else if (v.IsHolding<SdfLayerOffset>()) { w.U32(35); Offset(w, v.UncheckedGet<SdfLayerOffset>()); }
    else if (v.IsHolding<SdfVariantSelectionMap>())
    {
        const auto& map = v.UncheckedGet<SdfVariantSelectionMap>();
        Check(map.size() <= MaxItems, "Portable variant selection map exceeds 4096 items.");
        w.U32(36); w.U32(static_cast<uint32_t>(map.size()));
        for (const auto& item : map) { w.Text(item.first); w.Text(item.second); }
    }
    else if (v.IsHolding<SdfPath>()) { w.U32(37); PathValue(w, v.UncheckedGet<SdfPath>()); }
    else if (v.IsHolding<SdfPathVector>())
    {
        const auto& paths = v.UncheckedGet<SdfPathVector>();
        Check(paths.size() <= MaxItems, "Portable path vector exceeds 4096 items.");
        w.U32(38); w.U32(static_cast<uint32_t>(paths.size()));
        for (const auto& path : paths) { PathValue(w, path); }
    }
    else if (v.IsHolding<SdfRelocatesMap>())
    {
        const auto& map = v.UncheckedGet<SdfRelocatesMap>();
        Check(map.size() <= MaxItems, "Portable relocates map exceeds 4096 items.");
        w.U32(39); w.U32(static_cast<uint32_t>(map.size()));
        for (const auto& item : map) { PathValue(w, item.first); PathValue(w, item.second); }
    }
    else if (v.IsHolding<unsigned int>()) { w.U32(40); w.U32(v.UncheckedGet<unsigned int>()); }
    else if (v.IsHolding<uint64_t>()) { w.U32(41); w.U64(v.UncheckedGet<uint64_t>()); }
    else if (v.IsHolding<unsigned char>()) { w.U32(42); w.U32(v.UncheckedGet<unsigned char>()); }
    else if (v.IsHolding<GfHalf>()) { w.U32(43); Number(w, v.UncheckedGet<GfHalf>()); }
    else if (v.IsHolding<SdfTimeCode>()) { w.U32(44); w.F64(v.UncheckedGet<SdfTimeCode>().GetValue()); }
    else if (v.IsHolding<SdfPermission>()) { w.U32(45); w.U32(static_cast<uint32_t>(v.UncheckedGet<SdfPermission>())); }
#define PORTABLE_VECTOR(TYPE, TAG, N) \
    else if (v.IsHolding<TYPE>()) { w.U32(TAG); Vector<TYPE, N>(w, v.UncheckedGet<TYPE>()); }
    PORTABLE_VECTOR(GfVec2d, 46, 2)
    PORTABLE_VECTOR(GfVec4d, 47, 4)
    PORTABLE_VECTOR(GfVec2h, 48, 2)
    PORTABLE_VECTOR(GfVec3h, 49, 3)
    PORTABLE_VECTOR(GfVec4h, 50, 4)
    PORTABLE_VECTOR(GfVec2i, 51, 2)
    PORTABLE_VECTOR(GfVec3i, 52, 3)
    PORTABLE_VECTOR(GfVec4i, 53, 4)
#undef PORTABLE_VECTOR
    else if (v.IsHolding<GfMatrix2d>()) { w.U32(54); Matrix<GfMatrix2d, 2>(w, v.UncheckedGet<GfMatrix2d>()); }
    else if (v.IsHolding<GfMatrix3d>()) { w.U32(55); Matrix<GfMatrix3d, 3>(w, v.UncheckedGet<GfMatrix3d>()); }
    else if (v.IsHolding<GfQuatd>()) { w.U32(56); Quaternion(w, v.UncheckedGet<GfQuatd>()); }
    else if (v.IsHolding<GfQuath>()) { w.U32(57); Quaternion(w, v.UncheckedGet<GfQuath>()); }
    else if (v.IsHolding<SdfStringListOp>()) { w.U32(58); List(w, v.UncheckedGet<SdfStringListOp>(), depth); }
    else if (v.IsHolding<SdfIntListOp>()) { w.U32(59); List(w, v.UncheckedGet<SdfIntListOp>(), depth); }
    else if (v.IsHolding<SdfInt64ListOp>()) { w.U32(60); List(w, v.UncheckedGet<SdfInt64ListOp>(), depth); }
    else if (v.IsHolding<SdfUIntListOp>()) { w.U32(61); List(w, v.UncheckedGet<SdfUIntListOp>(), depth); }
    else if (v.IsHolding<SdfUInt64ListOp>()) { w.U32(62); List(w, v.UncheckedGet<SdfUInt64ListOp>(), depth); }
    else if (Array<SdfAssetPath>(w, v, 9, depth) || Array<GfVec2f>(w, v, 10, depth)
        || Array<GfVec3f>(w, v, 11, depth) || Array<GfVec3d>(w, v, 12, depth)
        || Array<GfVec4f>(w, v, 13, depth) || Array<GfQuatf>(w, v, 14, depth)
        || Array<GfMatrix4d>(w, v, 15, depth)
        || Array<unsigned int>(w, v, 40, depth) || Array<uint64_t>(w, v, 41, depth)
        || Array<unsigned char>(w, v, 42, depth) || Array<GfHalf>(w, v, 43, depth)
        || Array<SdfTimeCode>(w, v, 44, depth) || Array<GfVec2d>(w, v, 46, depth)
        || Array<GfVec4d>(w, v, 47, depth) || Array<GfVec2h>(w, v, 48, depth)
        || Array<GfVec3h>(w, v, 49, depth) || Array<GfVec4h>(w, v, 50, depth)
        || Array<GfVec2i>(w, v, 51, depth) || Array<GfVec3i>(w, v, 52, depth)
        || Array<GfVec4i>(w, v, 53, depth) || Array<GfMatrix2d>(w, v, 54, depth)
        || Array<GfMatrix3d>(w, v, 55, depth) || Array<GfQuatd>(w, v, 56, depth)
        || Array<GfQuath>(w, v, 57, depth)) {}
    else { return false; }
    return true;
}

VtValue DecodePortableValue(Reader& r, uint32_t tag, size_t depth)
{
    switch (tag)
    {
    case 32: return List<SdfReference>(r, depth);
    case 33: return List<SdfPayload>(r, depth);
    case 34:
    {
        const auto count = r.Count(); r.Need(static_cast<size_t>(count) * 16);
        std::vector<SdfLayerOffset> offsets; offsets.reserve(count);
        for (uint32_t i = 0; i < count; ++i) { offsets.push_back(Offset(r)); }
        return VtValue(offsets);
    }
    case 35: return VtValue(Offset(r));
    case 36:
    {
        const auto count = r.Count(); r.Need(static_cast<size_t>(count) * 8);
        SdfVariantSelectionMap map;
        for (uint32_t i = 0; i < count; ++i)
        {
            const auto key = r.Text(), value = r.Text();
            Check(map.emplace(key, value).second, "Duplicate portable variant selection.");
        }
        return VtValue(map);
    }
    case 37: return VtValue(PathValue(r));
    case 38:
    {
        const auto count = r.Count(); r.Need(static_cast<size_t>(count) * 4);
        SdfPathVector paths; paths.reserve(count);
        for (uint32_t i = 0; i < count; ++i) { paths.push_back(PathValue(r)); }
        return VtValue(paths);
    }
    case 39:
    {
        const auto count = r.Count(); r.Need(static_cast<size_t>(count) * 8);
        SdfRelocatesMap map;
        for (uint32_t i = 0; i < count; ++i)
        {
            const auto from = PathValue(r), to = PathValue(r);
            Check(map.emplace(from, to).second, "Duplicate portable relocate source.");
        }
        return VtValue(map);
    }
    case 40: return VtValue(static_cast<unsigned int>(r.U32()));
    case 41: return VtValue(r.U64());
    case 42: return VtValue(static_cast<unsigned char>(r.Count(255)));
    case 43: return VtValue(Number<GfHalf>(r));
    case 44: return VtValue(SdfTimeCode(r.F64()));
    case 45: return VtValue(static_cast<SdfPermission>(r.Count(1)));
#define PORTABLE_VECTOR(TYPE, TAG, N) case TAG: return Vector<TYPE, N>(r)
    PORTABLE_VECTOR(GfVec2d, 46, 2);
    PORTABLE_VECTOR(GfVec4d, 47, 4);
    PORTABLE_VECTOR(GfVec2h, 48, 2);
    PORTABLE_VECTOR(GfVec3h, 49, 3);
    PORTABLE_VECTOR(GfVec4h, 50, 4);
    PORTABLE_VECTOR(GfVec2i, 51, 2);
    PORTABLE_VECTOR(GfVec3i, 52, 3);
    PORTABLE_VECTOR(GfVec4i, 53, 4);
#undef PORTABLE_VECTOR
    case 54: return Matrix<GfMatrix2d, 2>(r);
    case 55: return Matrix<GfMatrix3d, 3>(r);
    case 56: return Quaternion<GfQuatd>(r);
    case 57: return Quaternion<GfQuath>(r);
    case 58: return List<std::string>(r, depth);
    case 59: return List<int>(r, depth);
    case 60: return List<int64_t>(r, depth);
    case 61: return List<unsigned int>(r, depth);
    case 62: return List<uint64_t>(r, depth);
#define PORTABLE_ARRAY(TYPE, TAG) case 256 + TAG: return Array<TYPE>(r, depth)
    PORTABLE_ARRAY(SdfAssetPath, 9);
    PORTABLE_ARRAY(GfVec2f, 10);
    PORTABLE_ARRAY(GfVec3f, 11);
    PORTABLE_ARRAY(GfVec3d, 12);
    PORTABLE_ARRAY(GfVec4f, 13);
    PORTABLE_ARRAY(GfQuatf, 14);
    PORTABLE_ARRAY(GfMatrix4d, 15);
    PORTABLE_ARRAY(unsigned int, 40);
    PORTABLE_ARRAY(uint64_t, 41);
    PORTABLE_ARRAY(unsigned char, 42);
    PORTABLE_ARRAY(GfHalf, 43);
    PORTABLE_ARRAY(SdfTimeCode, 44);
    PORTABLE_ARRAY(GfVec2d, 46);
    PORTABLE_ARRAY(GfVec4d, 47);
    PORTABLE_ARRAY(GfVec2h, 48);
    PORTABLE_ARRAY(GfVec3h, 49);
    PORTABLE_ARRAY(GfVec4h, 50);
    PORTABLE_ARRAY(GfVec2i, 51);
    PORTABLE_ARRAY(GfVec3i, 52);
    PORTABLE_ARRAY(GfVec4i, 53);
    PORTABLE_ARRAY(GfMatrix2d, 54);
    PORTABLE_ARRAY(GfMatrix3d, 55);
    PORTABLE_ARRAY(GfQuatd, 56);
    PORTABLE_ARRAY(GfQuath, 57);
#undef PORTABLE_ARRAY
    default: throw std::runtime_error("Unsupported portable concrete authored value tag.");
    }
}
}
