// Copyright (c) marcschier. Licensed under the MIT License.
#include "internal/layer_edit.h"

namespace OpenUsdEdit
{
namespace
{
template <class T> struct Scalar;
#define EDIT_SCALAR(TYPE, TAG, WRITE, READ) \
    template <> struct Scalar<TYPE> { \
        static constexpr uint32_t tag = TAG; \
        static void Put(Writer& w, const TYPE& v) { WRITE; } \
        static TYPE Get(Reader& r) { return READ; } \
    }
EDIT_SCALAR(bool, 2, w.U32(v ? 1 : 0), r.Count(1) != 0);
EDIT_SCALAR(int, 3, w.U32(static_cast<uint32_t>(v)), static_cast<int>(r.U32()));
EDIT_SCALAR(int64_t, 4, w.U64(static_cast<uint64_t>(v)), static_cast<int64_t>(r.U64()));
EDIT_SCALAR(float, 5, w.F32(v), r.F32());
EDIT_SCALAR(double, 6, w.F64(v), r.F64());
EDIT_SCALAR(TfToken, 7, w.Text(v.GetString()), TfToken(r.Text()));
EDIT_SCALAR(std::string, 8, w.Text(v), r.Text());
#undef EDIT_SCALAR

template <class T>
bool PutScalar(Writer& w, const VtValue& value)
{
    if (value.IsHolding<T>())
    {
        w.U32(Scalar<T>::tag);
        Scalar<T>::Put(w, value.UncheckedGet<T>());
        return true;
    }
    if (value.IsHolding<VtArray<T>>())
    {
        const auto& array = value.UncheckedGet<VtArray<T>>();
        Check(array.size() <= MaxItems, "Editing array element budget exceeded (4096).");
        w.U32(256 + Scalar<T>::tag);
        w.U32(static_cast<uint32_t>(array.size()));
        for (const auto& item : array) { Scalar<T>::Put(w, item); }
        return true;
    }
    return false;
}

template <class T>
VtValue GetArray(Reader& r)
{
    const uint32_t count = r.Count();
    r.Need(count * 4ull);
    VtArray<T> array(count);
    for (auto& item : array) { item = Scalar<T>::Get(r); }
    return VtValue(std::move(array));
}

template <class T>
void PutItems(Writer& w, const std::vector<T>& items)
{
    Check(items.size() <= MaxItems, "Editing list item budget exceeded (4096).");
    w.U32(static_cast<uint32_t>(items.size()));
    for (const auto& item : items)
    {
        if constexpr (std::is_same_v<T, SdfPath>)
        {
            if (w.portable)
            {
                Check(item.GetPathElementCount() <= MaxDepth, "Portable list path exceeds 32 elements.");
                AdmitPortablePathText(item.GetString());
            }
            w.Text(item.GetString());
        }
        else { Scalar<T>::Put(w, item); }
    }
}

template <class T>
std::vector<T> GetItems(Reader& r)
{
    const uint32_t count = r.Count();
    r.Need(count * 4ull);
    std::vector<T> items;
    items.reserve(count);
    for (uint32_t i = 0; i < count; ++i)
    {
        if constexpr (std::is_same_v<T, SdfPath>)
        {
            const std::string text = r.Text();
            if (r.portable) { AdmitPortablePathText(text); }
            Check(SdfPath::IsValidPathString(text), "Invalid list-op SdfPath.");
            items.emplace_back(text);
        }
        else { items.push_back(Scalar<T>::Get(r)); }
    }
    return items;
}

template <class T>
void PutList(Writer& w, const SdfListOp<T>& list)
{
    if (w.portable)
    {
        Check(list.GetExplicitItems().size() + list.GetAddedItems().size() + list.GetPrependedItems().size()
            + list.GetAppendedItems().size() + list.GetDeletedItems().size() + list.GetOrderedItems().size() <= MaxItems,
            "Portable list-op exceeds 4096 items across all buckets.");
    }
    w.U32(list.IsExplicit() ? 1 : 0);
    PutItems(w, list.GetExplicitItems());
    PutItems(w, list.GetAddedItems());
    PutItems(w, list.GetPrependedItems());
    PutItems(w, list.GetAppendedItems());
    PutItems(w, list.GetDeletedItems());
    PutItems(w, list.GetOrderedItems());
}

template <class T>
VtValue GetList(Reader& r)
{
    const bool explicitMode = r.Count(1) != 0;
    const auto explicitItems = GetItems<T>(r);
    const auto added = GetItems<T>(r);
    const auto prepended = GetItems<T>(r);
    const auto appended = GetItems<T>(r);
    const auto deleted = GetItems<T>(r);
    const auto ordered = GetItems<T>(r);
    Check(!r.portable || explicitItems.size() + added.size() + prepended.size()
        + appended.size() + deleted.size() + ordered.size() <= MaxItems,
        "Portable list-op exceeds 4096 items across all buckets.");
    for (const auto* items : {&explicitItems, &added, &prepended, &appended, &deleted, &ordered})
    {
        Check(std::set<T>(items->begin(), items->end()).size() == items->size(),
            "Duplicate list-op bucket item.");
    }
    SdfListOp<T> list;
    if (explicitMode)
    {
        Check(added.empty() && prepended.empty() && appended.empty()
            && deleted.empty() && ordered.empty(), "Mixed explicit/non-explicit list operation.");
        list.SetExplicitItems(explicitItems);
    }
    else
    {
        Check(explicitItems.empty(), "Non-explicit list has explicit items.");
        list.SetAddedItems(added);
        list.SetPrependedItems(prepended);
        list.SetAppendedItems(appended);
        list.SetDeletedItems(deleted);
        list.SetOrderedItems(ordered);
    }
    return VtValue(list);
}

template <class T, int N>
void PutVector(Writer& w, const T& v)
{
    for (int i = 0; i < N; ++i)
    {
        if constexpr (std::is_same_v<typename T::ScalarType, double>) { w.F64(v[i]); }
        else { w.F32(v[i]); }
    }
}

template <class T, int N>
VtValue GetVector(Reader& r)
{
    T v;
    for (int i = 0; i < N; ++i)
    {
        if constexpr (std::is_same_v<typename T::ScalarType, double>) { v[i] = r.F64(); }
        else { v[i] = r.F32(); }
    }
    return VtValue(v);
}
}

void EncodeValue(Writer& w, const VtValue& v, size_t depth)
{
    Check(depth < 16, "Editing value nesting budget exceeded (16).");
    Check(!v.IsArrayEditValued(), "VtArrayEdit instructions are not supported for editing.");
    if (v.IsEmpty()) { w.U32(0); }
    else if (v.IsHolding<SdfValueBlock>()) { w.U32(1); }
    else if (PutScalar<bool>(w, v) || PutScalar<int>(w, v)
        || PutScalar<int64_t>(w, v) || PutScalar<float>(w, v)
        || PutScalar<double>(w, v) || PutScalar<TfToken>(w, v)
        || PutScalar<std::string>(w, v)) {}
    else if (v.IsHolding<SdfAssetPath>())
    {
        w.U32(9);
        const auto& asset = v.UncheckedGet<SdfAssetPath>();
        w.Text(asset.GetAuthoredPath());
        w.Text(asset.GetEvaluatedPath());
        w.Text(asset.GetResolvedPath());
    }
    else if (v.IsHolding<GfVec2f>())
    {
        w.U32(10); PutVector<GfVec2f, 2>(w, v.UncheckedGet<GfVec2f>());
    }
    else if (v.IsHolding<GfVec3f>())
    {
        w.U32(11); PutVector<GfVec3f, 3>(w, v.UncheckedGet<GfVec3f>());
    }
    else if (v.IsHolding<GfVec3d>())
    {
        w.U32(12); PutVector<GfVec3d, 3>(w, v.UncheckedGet<GfVec3d>());
    }
    else if (v.IsHolding<GfVec4f>())
    {
        w.U32(13); PutVector<GfVec4f, 4>(w, v.UncheckedGet<GfVec4f>());
    }
    else if (v.IsHolding<GfQuatf>())
    {
        w.U32(14);
        const auto& q = v.UncheckedGet<GfQuatf>();
        w.F32(q.GetReal());
        PutVector<GfVec3f, 3>(w, q.GetImaginary());
    }
    else if (v.IsHolding<GfMatrix4d>())
    {
        w.U32(15);
        const auto& matrix = v.UncheckedGet<GfMatrix4d>();
        for (int row = 0; row < 4; ++row)
        {
            for (int column = 0; column < 4; ++column) { w.F64(matrix[row][column]); }
        }
    }
    else if (v.IsHolding<SdfPathListOp>())
    {
        w.U32(16); PutList(w, v.UncheckedGet<SdfPathListOp>());
    }
    else if (v.IsHolding<TfTokenVector>())
    {
        w.U32(17); PutItems(w, v.UncheckedGet<TfTokenVector>());
    }
    else if (v.IsHolding<std::vector<std::string>>())
    {
        w.U32(18); PutItems(w, v.UncheckedGet<std::vector<std::string>>());
    }
    else if (v.IsHolding<SdfSpecifier>())
    {
        w.U32(19); w.U32(static_cast<uint32_t>(v.UncheckedGet<SdfSpecifier>()));
    }
    else if (v.IsHolding<SdfVariability>())
    {
        w.U32(20); w.U32(static_cast<uint32_t>(v.UncheckedGet<SdfVariability>()));
    }
    else if (v.IsHolding<SdfTimeSampleMap>())
    {
        w.U32(21);
        const auto& samples = v.UncheckedGet<SdfTimeSampleMap>();
        Check(samples.size() <= MaxItems, "Checkpoint time-sample budget exceeded.");
        w.U32(static_cast<uint32_t>(samples.size()));
        for (const auto& sample : samples)
        {
            Check(std::isfinite(sample.first), "Non-finite authored time sample.");
            w.F64(sample.first);
            EncodeValue(w, sample.second, depth + 1);
        }
    }
    else if (v.IsHolding<VtDictionary>())
    {
        w.U32(22);
        const auto& dictionary = v.UncheckedGet<VtDictionary>();
        Check(dictionary.size() <= MaxItems, "Checkpoint dictionary entry budget exceeded.");
        w.U32(static_cast<uint32_t>(dictionary.size()));
        for (const auto& item : dictionary)
        {
            w.Text(item.first);
            EncodeValue(w, item.second, depth + 1);
        }
    }
    else if (v.IsHolding<SdfTokenListOp>())
    {
        w.U32(23); PutList(w, v.UncheckedGet<SdfTokenListOp>());
    }
    else if (w.portable && EncodePortableValue(w, v, depth)) {}
    else
    {
        throw std::runtime_error("Unsupported concrete authored value; no mutation performed.");
    }
}

VtValue DecodeValue(Reader& r, size_t depth)
{
    Check(depth < 16, "Editing value nesting budget exceeded (16).");
    const uint32_t tag = r.U32();
    switch (tag)
    {
    case 0: return {};
    case 1: return VtValue(SdfValueBlock());
#define EDIT_READ(TYPE, TAG) \
    case TAG: return VtValue(Scalar<TYPE>::Get(r)); \
    case 256 + TAG: return GetArray<TYPE>(r)
    EDIT_READ(bool, 2);
    EDIT_READ(int, 3);
    EDIT_READ(int64_t, 4);
    EDIT_READ(float, 5);
    EDIT_READ(double, 6);
    EDIT_READ(TfToken, 7);
    EDIT_READ(std::string, 8);
#undef EDIT_READ
    case 9:
    {
        const auto authored = r.Text();
        const auto evaluated = r.Text();
        const auto resolved = r.Text();
        TfErrorMark mark;
        const SdfAssetPath asset(SdfAssetPathParams().Authored(authored)
            .Evaluated(evaluated).Resolved(resolved));
        if (!mark.IsClean())
        {
            ConsumeErrors(mark);
            throw std::runtime_error("Invalid UTF-8/control character in asset path.");
        }
        return VtValue(asset);
    }
    case 10: return GetVector<GfVec2f, 2>(r);
    case 11: return GetVector<GfVec3f, 3>(r);
    case 12: return GetVector<GfVec3d, 3>(r);
    case 13: return GetVector<GfVec4f, 4>(r);
    case 14:
    {
        const float real = r.F32();
        const auto imaginary = GetVector<GfVec3f, 3>(r);
        return VtValue(GfQuatf(real, imaginary.UncheckedGet<GfVec3f>()));
    }
    case 15:
    {
        GfMatrix4d matrix;
        for (int row = 0; row < 4; ++row)
        {
            for (int column = 0; column < 4; ++column) { matrix[row][column] = r.F64(); }
        }
        return VtValue(matrix);
    }
    case 16: return GetList<SdfPath>(r);
    case 17: return VtValue(GetItems<TfToken>(r));
    case 18: return VtValue(GetItems<std::string>(r));
    case 19: return VtValue(static_cast<SdfSpecifier>(r.Count(2)));
    case 20: return VtValue(static_cast<SdfVariability>(r.Count(1)));
    case 21:
    {
        const uint32_t count = r.Count();
        r.Need(count * 12ull);
        SdfTimeSampleMap samples;
        for (uint32_t i = 0; i < count; ++i)
        {
            const double time = r.F64();
            Check(std::isfinite(time), "Non-finite sample time.");
            auto value = DecodeValue(r, depth + 1);
            Check(!value.IsEmpty() && samples.emplace(time, std::move(value)).second,
                "Empty or duplicate time sample.");
        }
        return VtValue(std::move(samples));
    }
    case 22:
    {
        const uint32_t count = r.Count();
        r.Need(count * 8ull);
        VtDictionary dictionary;
        for (uint32_t i = 0; i < count; ++i)
        {
            const auto key = r.Text();
            auto value = DecodeValue(r, depth + 1);
            Check(dictionary.insert(std::make_pair(key, std::move(value))).second,
                "Duplicate dictionary key.");
        }
        return VtValue(std::move(dictionary));
    }
    case 23: return GetList<TfToken>(r);
    default:
        if (r.portable) { return DecodePortableValue(r, tag, depth); }
        throw std::runtime_error("Unsupported editing value tag.");
    }
}

bool SameValue(const VtValue& a, const VtValue& b)
{
    Writer left;
    Writer right;
    EncodeValue(left, a);
    EncodeValue(right, b);
    return left.bytes == right.bytes;
}

void Identity::Write(Writer& w) const
{
    w.U64(stage); w.U64(layer); w.U64(generation); w.U64(revision);
}

Identity Identity::Read(Reader& r)
{
    Identity value;
    value.stage = r.U64();
    value.layer = r.U64();
    value.generation = r.U64();
    value.revision = r.U64();
    Check(value.stage && value.layer && value.generation, "Invalid editing identity.");
    return value;
}

void Address::Write(Writer& w) const
{
    w.Text(path.GetString()); w.U32(field); w.F64(time);
}

Address Address::Read(Reader& r)
{
    Address address;
    const auto text = r.Text();
    Check(!text.empty() && SdfPath::IsValidPathString(text), "Invalid editing path.");
    address.path = SdfPath(text);
    Check(address.path.IsAbsolutePath() && address.path.IsPrimPropertyPath()
        && !address.path.ContainsPrimVariantSelection()
        && address.path.GetPathElementCount() <= MaxDepth,
        "Editing requires an absolute, non-variant direct prim-property path of at most 32 elements.");
    address.field = r.Count(3);
    address.time = r.F64();
    Check(std::isfinite(address.time) && (address.field == 1 || address.time == 0),
        "A sample requires finite numeric time; other fields require zero time.");
    if (address.time == 0) { address.time = 0; }
    return address;
}

bool Address::operator==(const Address& other) const
{
    return path == other.path && field == other.field && time == other.time;
}

void Opinion::Write(Writer& w) const
{
    address.Write(w);
    w.U32(kind);
    EncodeValue(w, typeName);
    EncodeValue(w, variability);
    EncodeValue(w, custom);
    EncodeValue(w, value);
}

Opinion Opinion::Read(Reader& r)
{
    Opinion value;
    value.address = Address::Read(r);
    value.kind = r.Count(2);
    value.typeName = DecodeValue(r);
    value.variability = DecodeValue(r);
    value.custom = DecodeValue(r);
    value.value = DecodeValue(r);
    Check((value.typeName.IsEmpty() || value.typeName.IsHolding<TfToken>())
        && (value.variability.IsEmpty() || value.variability.IsHolding<SdfVariability>())
        && (value.custom.IsEmpty() || value.custom.IsHolding<bool>()),
        "Invalid raw property declaration.");
    Check(value.kind != 0 || (value.typeName.IsEmpty() && value.variability.IsEmpty()
        && value.custom.IsEmpty() && value.value.IsEmpty()), "Absent property contains opinions.");
    return value;
}

std::vector<uint8_t> Snapshot::Bytes() const
{
    Writer w;
    w.Header(2);
    identity.Write(w);
    Check(opinions.size() <= MaxAddresses, "Too many snapshot addresses.");
    w.U32(static_cast<uint32_t>(opinions.size()));
    for (const auto& opinion : opinions) { opinion.Write(w); }
    return std::move(w.bytes);
}

Snapshot Snapshot::Read(const uint8_t* data, size_t size)
{
    Reader r(data, size);
    r.Header(2);
    Snapshot value;
    value.identity = Identity::Read(r);
    const uint32_t count = r.Count(MaxAddresses);
    Check(count != 0, "An authored snapshot must have addresses.");
    r.Need(count * 36ull);
    for (uint32_t i = 0; i < count; ++i)
    {
        auto opinion = Opinion::Read(r);
        for (const auto& previous : value.opinions)
        {
            Check(!(previous.address == opinion.address), "Duplicate snapshot address.");
        }
        value.opinions.push_back(std::move(opinion));
    }
    r.End();
    return value;
}

std::vector<Address> Addresses(const Snapshot& snapshot)
{
    std::vector<Address> addresses;
    addresses.reserve(snapshot.opinions.size());
    for (const auto& opinion : snapshot.opinions) { addresses.push_back(opinion.address); }
    return addresses;
}

Snapshot Capture(const openusd_layer* handle, const std::vector<Address>& addresses)
{
    auto& record = Record(handle);
    Check(record.local, "Cannot capture a detached layer.");
    const auto data = Resident(record.layer);
    Snapshot snapshot;
    snapshot.identity = GetIdentity(handle->stage, record);
    for (const auto& address : addresses)
    {
        Opinion opinion;
        opinion.address = address;
        const auto type = data->GetSpecType(address.path);
        Check(type == SdfSpecTypeUnknown || type == SdfSpecTypeAttribute
            || type == SdfSpecTypeRelationship, "Unsupported target property spec kind.");
        opinion.kind = type == SdfSpecTypeAttribute ? 1u : type == SdfSpecTypeRelationship ? 2u : 0u;
        if (opinion.kind)
        {
            Check((opinion.kind == 2) == (address.field == 3),
                "The requested field does not match the target property kind.");
            data->Has(address.path, SdfFieldKeys->TypeName, &opinion.typeName);
            data->Has(address.path, SdfFieldKeys->Variability, &opinion.variability);
            data->Has(address.path, SdfFieldKeys->Custom, &opinion.custom);
            Check((opinion.typeName.IsEmpty() || opinion.typeName.IsHolding<TfToken>())
                && (opinion.variability.IsEmpty() || opinion.variability.IsHolding<SdfVariability>())
                && (opinion.custom.IsEmpty() || opinion.custom.IsHolding<bool>()),
                "Unsupported raw declaration value type.");
            if (address.field == 1)
            {
                VtValue samples;
                if (data->Has(address.path, SdfFieldKeys->TimeSamples, &samples))
                {
                    Check(samples.IsHolding<SdfTimeSampleMap>()
                        && !samples.UncheckedGet<SdfTimeSampleMap>().empty(),
                        "Explicit empty or unsupported time-sample containers are not addressable.");
                }
                data->QueryTimeSample(address.path, address.time, &opinion.value);
            }
            else
            {
                const TfToken& key = address.field == 0 ? SdfFieldKeys->Default
                    : address.field == 2 ? SdfFieldKeys->ConnectionPaths : SdfFieldKeys->TargetPaths;
                data->Has(address.path, key, &opinion.value);
            }
        }
        snapshot.opinions.push_back(std::move(opinion));
    }
    return snapshot;
}
}
