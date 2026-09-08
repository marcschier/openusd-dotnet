// Copyright (c) marcschier. Licensed under the MIT License.
#pragma once

#include "common.h"
#include "openusd_layer_edit.h"
#include "pxr/usd/sdf/data.h"
#include "pxr/usd/sdf/fileFormat.h"
#include "pxr/usd/sdf/notice.h"
#include "pxr/usd/sdf/usdaData.h"

#include <map>
#include <set>

namespace OpenUsdEdit
{
constexpr size_t MaxBytes = OPENUSD_LAYER_EDIT_MAX_BYTES;
constexpr size_t MaxString = OPENUSD_LAYER_EDIT_MAX_STRING_BYTES;
constexpr size_t MaxItems = OPENUSD_LAYER_EDIT_MAX_ITEMS;
constexpr size_t MaxAddresses = OPENUSD_LAYER_EDIT_MAX_ADDRESSES;
constexpr size_t MaxSpecs = OPENUSD_LAYER_EDIT_MAX_SPECS;
constexpr size_t MaxFields = OPENUSD_LAYER_EDIT_MAX_FIELDS;
constexpr size_t MaxDepth = OPENUSD_LAYER_EDIT_MAX_PATH_ELEMENTS;
constexpr uint32_t Magic = 0x31444555; // UED1

inline void Check(bool condition, const char* message)
{
    if (!condition)
    {
        throw std::runtime_error(message);
    }
}

struct Writer
{
    std::vector<uint8_t> bytes;
    bool portable = false;
    void Need(size_t size) const;
    void U32(uint32_t value);
    void U64(uint64_t value);
    void F32(float value);
    void F64(double value);
    void Text(std::string_view value);
    void Header(uint32_t kind);
};

struct Reader
{
    const uint8_t* data;
    size_t size;
    size_t offset = 0;
    bool portable;
    Reader(const uint8_t* bytes, size_t count, bool portableValues = false);
    void Need(size_t count) const;
    uint32_t U32();
    uint64_t U64();
    float F32();
    double F64();
    std::string Text();
    uint32_t Count(size_t limit = MaxItems);
    void Header(uint32_t kind);
    void End() const;
};

void EncodeValue(Writer& writer, const VtValue& value, size_t depth = 0);
VtValue DecodeValue(Reader& reader, size_t depth = 0);
void AdmitPortablePathText(std::string_view text);
bool SameValue(const VtValue& a, const VtValue& b);
std::string InputText(const char* value, size_t size);
bool ValidUtf8(std::string_view value);
bool EncodePortableValue(Writer& writer, const VtValue& value, size_t depth);
VtValue DecodePortableValue(Reader& reader, uint32_t tag, size_t depth);

// Only this owned data implementation can supply a non-allocating field-count
// preflight. SDK SdfData::List() copies an unbounded field-name vector.
class CountedData final : public SdfData
{
public:
    void CreateSpec(const SdfPath& path, SdfSpecType type) override;
    void EraseSpec(const SdfPath& path) override;
    void MoveSpec(const SdfPath& from, const SdfPath& to) override;
    void Set(const SdfPath& path, const TfToken& field, const VtValue& value) override;
    void Set(const SdfPath& path, const TfToken& field,
        const SdfAbstractDataConstValue& value) override;
    void Erase(const SdfPath& path, const TfToken& field) override;
    void SetTimeSample(const SdfPath& path, double time, const VtValue& value) override;
    void EraseTimeSample(const SdfPath& path, double time) override;
    size_t FieldCount(const SdfPath& path) const;
    const std::map<SdfPath, size_t>& Inventory() const { return _fields; }
    uint64_t Version() const { return _version; }
private:
    void Changed(const SdfPath& path, const TfToken& field, bool before, size_t previousCount);
    std::map<SdfPath, size_t> _fields;
    uint64_t _version = 0;
};

struct DataAccess : SdfFileFormat
{
    static SdfAbstractDataConstPtr Get(const SdfLayer& layer)
    {
        return _GetLayerData(layer);
    }
    static void Install(SdfLayer* layer, SdfAbstractDataRefPtr data)
    {
        _SetLayerData(layer, data);
    }
};

SdfAbstractDataConstPtr Resident(const SdfLayerHandle& layer);

struct Identity
{
    uint64_t stage = 0;
    uint64_t layer = 0;
    uint64_t generation = 0;
    uint64_t revision = 0;
    void Write(Writer& writer) const;
    static Identity Read(Reader& reader);
};

struct Address
{
    SdfPath path;
    uint32_t field = 0;
    double time = 0;
    void Write(Writer& writer) const;
    static Address Read(Reader& reader);
    bool operator==(const Address& other) const;
};

struct Opinion
{
    Address address;
    uint32_t kind = 0; // absent, attribute, relationship
    VtValue typeName;
    VtValue variability;
    VtValue custom;
    VtValue value; // empty means absent, SdfValueBlock means block
    void Write(Writer& writer) const;
    static Opinion Read(Reader& reader);
};

struct Snapshot
{
    Identity identity;
    std::vector<Opinion> opinions;
    std::vector<uint8_t> Bytes() const;
    static Snapshot Read(const uint8_t* data, size_t size);
};

class LayerRecord final : public TfWeakBase
{
public:
    explicit LayerRecord(SdfLayerRefPtr value, uint32_t roleValue);
    ~LayerRecord();
    SdfLayerRefPtr layer;
    SdfAbstractDataConstRefPtr backing;
    uint64_t id;
    uint64_t generation = 1;
    uint64_t revision = 1;
    uint64_t savedRevision = 0;
    uint32_t role;
    bool local = true;
    bool writing = false;
    std::map<SdfPath, bool> owned; // true if a foreign edit touched this owned spec
private:
    void Changed(const SdfNotice::LayersDidChangeSentPerLayer&);
    void Replaced(const SdfNotice::LayerDidReplaceContent&);
    void Renamed(const SdfNotice::LayerIdentifierDidChange&);
    void Saved(const SdfNotice::LayerDidSaveLayerToFile&);
    TfNotice::Key _changed;
    TfNotice::Key _replaced;
    TfNotice::Key _renamed;
    TfNotice::Key _saved;
};

void Publish(std::vector<uint8_t> bytes, openusd_edit_buffer** owner,
    openusd_edit_buffer_view* view);
void PublishPortable(std::vector<uint8_t> bytes, openusd_edit_buffer** owner,
    openusd_edit_buffer_view* view);
void Outputs(openusd_edit_buffer** owner, openusd_edit_buffer_view* view);
template <class Action>
openusd_status GuardBuffer(const openusd_layer* layer, openusd_edit_buffer** owner,
    openusd_edit_buffer_view* view, openusd_error_buffer* error, Action&& action)
{
    ResetAbiOutput(owner);
    ResetAbiOutput(view);
    const auto status = GuardLayer(layer, error, std::forward<Action>(action));
    if (status != OPENUSD_STATUS_OK)
    {
        if (owner)
        {
            openusd_edit_buffer* pending = nullptr;
            std::memcpy(&pending, owner, sizeof(pending));
            openusd_edit_buffer_release(pending);
        }
        ResetAbiOutput(owner);
        ResetAbiOutput(view);
    }
    return status;
}
Snapshot Capture(const openusd_layer* handle, const std::vector<Address>& addresses);
std::vector<Address> Addresses(const Snapshot& snapshot);
Identity GetIdentity(const openusd_stage* stage, const LayerRecord& record);
LayerRecord& Record(const openusd_layer* layer);
bool Matches(const Identity& identity, const openusd_stage* stage, const LayerRecord& record);
void RegisterOverlay(const openusd_stage* stage, const SdfLayerRefPtr& physics,
    const SdfLayerRefPtr& user);
void InitializeReviewData(const SdfLayerRefPtr& newlyCreatedLayer);
}

class OpenUsdReviewState;

class OpenUsdEditContext final : public TfWeakBase
{
public:
    explicit OpenUsdEditContext(const openusd_stage* stage);
    ~OpenUsdEditContext();
    OpenUsdEdit::LayerRecord& Track(const SdfLayerHandle& layer, uint32_t role = 4);
    void Refresh();
    const openusd_stage* stage;
    uint64_t id;
    std::vector<std::unique_ptr<OpenUsdEdit::LayerRecord>> records;
    SdfLayerRefPtr user;
    SdfLayerRefPtr physics;
    std::shared_ptr<OpenUsdReviewState> review;
private:
    void Changed(const UsdNotice::StageContentsChanged&);
    TfNotice::Key _key;
};

OpenUsdEditContext& EditContext(const openusd_stage* stage);
