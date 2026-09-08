// Copyright (c) marcschier. Licensed under the MIT License.
#pragma once

#include "layer_edit.h"
#include "openusd_review_document.h"
#include "pxr/usd/ar/resolverContext.h"

#include <array>
#include <functional>

// Portable wire (all integer and IEEE words little-endian; Text = u32 byte
// length + strict UTF-8 without NUL):
// RSB1: magic/version, u64 stage identity, Text root/fingerprint/anchor, files.
// RDE1: magic/version, Blob URD1, Blob process-local URR1 receipt (empty on
// read), Text documentId/root/fingerprint/originalTarget/target/anchor, files.
// Files: u32 count then Text path/hash, u64 byteLength, u32 kind (layer=0,
// asset=1). Sorted unique absolute filesystem paths, including the root.
// URD1: magic/version, u32 payloadLength, u32 reserved=0, SHA256(payload)[32],
// then the RDE1 Text metadata/files followed by Blob RVW1. No UED1 identities.
// RVW1: magic/version/specCount, then Text path, u32 SdfSpecType, fieldCount,
// repeated Text field + exact typed value. Canonical spec/field order; the
// portable-only extensions in review_values.cpp do not extend old UED1.
// URR1: magic/version/Text random GUID. Only the originating process/session
// holds the receipt identity, revision, exact review and manifest digests.
// Source bytes: <=16MiB/file, <=64MiB per manifest, separately from packets.
// Source parse work is additionally bounded by the public token/spec/edge/
// composition limits. Windows scoped read leases keep admitted filenames from
// disappearing during default-resolver look-here-first resolution. Physical
// alias policy/publication stays with the caller; no final user file is written.
// SDK proof: pinned _CreateNew holds the registry lock through initialization;
// SeedFormat calls the native _InstantiateNewLayer, then _SetLayerData swaps
// admitted parser data before _FinishInitialization. Original file format,
// root semantics, authored data and real anchors remain intact. Mutation
// epochs are sampled BEFORE publication. Unknown existing caches are refused.

namespace OpenUsdReview
{
using Bytes = std::vector<uint8_t>;
using OpenUsdEdit::Check;
using OpenUsdEdit::CountedData;
constexpr size_t MaxEnvelope = OPENUSD_REVIEW_MAX_ENVELOPE_BYTES;
constexpr size_t MaxDocument = OPENUSD_REVIEW_MAX_DOCUMENT_BYTES;
constexpr size_t MaxReview = OPENUSD_REVIEW_MAX_REVIEW_BYTES;
constexpr size_t MaxFiles = OPENUSD_REVIEW_MAX_MANIFEST_FILES;
constexpr size_t MaxFileBytes = OPENUSD_REVIEW_MAX_SOURCE_FILE_BYTES;
constexpr size_t MaxSourceBytes = OPENUSD_REVIEW_MAX_SOURCE_TOTAL_BYTES;
constexpr size_t MaxSourceTokens = OPENUSD_REVIEW_MAX_SOURCE_TOKENS;
constexpr size_t MaxSourceSpecs = OPENUSD_REVIEW_MAX_SOURCE_SPECS;

class Conflict final : public std::runtime_error
{
public:
    using std::runtime_error::runtime_error;
};

class FileReadScope
{
public:
    FileReadScope();
    ~FileReadScope();
    static void Retain(const std::string& path);
private:
    struct Impl;
    std::unique_ptr<Impl> impl;
};

struct PacketWriter
{
    explicit PacketWriter(size_t maximum) : limit(maximum) {}
    size_t limit;
    Bytes bytes;
    void U32(uint32_t value);
    void U64(uint64_t value);
    void Raw(const uint8_t* data, size_t size);
    void Blob(const Bytes& value);
    void Text(std::string_view value);
};

struct PacketReader
{
    PacketReader(const uint8_t* data, size_t size, size_t maximum);
    const uint8_t* data;
    size_t size;
    size_t offset = 0;
    void Need(size_t count) const;
    uint32_t U32();
    uint64_t U64();
    uint32_t Count(size_t maximum);
    std::string Text();
    Bytes Blob(size_t maximum);
    void End() const;
};

std::array<uint8_t, 32> Sha256(const uint8_t* bytes, size_t size);
std::string Hash(const uint8_t* bytes, size_t size);
inline std::string Hash(const Bytes& bytes) { return Hash(bytes.data(), bytes.size()); }
std::string Guid();
bool IsGuid(const std::string& text);
std::string TextInput(const char* text, size_t size);
std::string Path(const std::string& text, bool existing);
std::string Parent(const std::string& path);
Bytes ReadFile(const std::string& path, size_t& total);
void CheckFilesystemPath(const std::string& text);
void DefaultResolver();
std::string ResolvePath(const std::string& raw, const std::string& anchorFile);
void ValidateComposition(const std::map<std::string, TfRefPtr<CountedData>>& layers,
    const std::vector<std::string>& roots);

struct File
{
    std::string path;
    std::string hash;
    uint64_t length = 0;
    uint32_t kind = 0;
    bool operator==(const File& other) const;
};

struct Source
{
    std::string root;
    std::string fingerprint;
    std::string anchor;
    std::vector<File> files;
};
void WriteFiles(PacketWriter& writer, const std::vector<File>& files);
std::vector<File> ReadFiles(PacketReader& reader, bool inspectionOnly = false);
void ValidateSource(const Source& source);
Bytes BindingBytes(uint64_t stageId, const Source& source);
bool MatchBinding(const openusd_stage* stage, const uint8_t* bytes, size_t size);
std::string ManifestHash(const std::vector<File>& files);

class Origin final : public TfWeakBase
{
public:
    Origin(const SdfLayerRefPtr& value, TfRefPtr<CountedData> content, File image,
        uint64_t admittedVersion, std::shared_ptr<const std::vector<File>> admittedFiles);
    ~Origin();
    void Verify() const;
    SdfLayerHandle layer;
    TfRefPtr<CountedData> data;
    File file;
    uint64_t version;
    std::shared_ptr<const std::vector<File>> proof;
private:
    void Changed(const SdfNotice::LayersDidChangeSentPerLayer&);
    void Replaced(const SdfNotice::LayerDidReplaceContent&);
    void Renamed(const SdfNotice::LayerIdentifierDidChange&);
    std::atomic<bool> changed{false};
    TfNotice::Key changeKey;
    TfNotice::Key replaceKey;
    TfNotice::Key renameKey;
};

struct Admission
{
    Source source;
    std::vector<SdfLayerRefPtr> layers;
    std::vector<std::shared_ptr<Origin>> origins;
    ArResolverContext resolver;
};
Admission Admit(const std::string& root);
void VerifySource(const openusd_stage* stage);
void VerifyFiles(const std::vector<File>& files);
TfRefPtr<CountedData> ParseText(const Bytes& bytes);
SdfLayerRefPtr SeedLayer(const std::string& identifier, const TfRefPtr<CountedData>& data,
    bool anonymous = false);
SdfLayerRefPtr NewReviewLayer(const openusd_stage* stage, TfRefPtr<CountedData> data = {});
void UserLayerAttached(const openusd_stage* stage);
TfRefPtr<CountedData> ReviewData(const openusd_layer* layer);
Bytes EncodeReview(const TfRefPtr<CountedData>& data, bool inspectionOnly = false);
TfRefPtr<CountedData> DecodeReview(const uint8_t* bytes, size_t size, bool inspectionOnly = false);
void VisitAssets(const VtValue& value,
    const std::function<void(const std::string&, bool)>& visit, size_t depth = 0);
std::vector<File> ReviewFiles(const openusd_stage* stage, const SdfLayerRefPtr& review,
    const TfRefPtr<CountedData>& data);
bool Pristine(const openusd_stage* stage);

struct Document
{
    Source source;
    std::string id;
    std::string originalTarget;
    std::string target;
    Bytes review;
};
Bytes EncodeDocument(const Document& document);
Document DecodeDocument(const uint8_t* bytes, size_t size, bool inspectionOnly = false);
Bytes Envelope(const Document& document, const Bytes& portable, const Bytes& receipt);
Bytes Inspection(const Document& document, size_t documentSize);
Bytes StateBytes(const openusd_stage* stage, OpenUsdEdit::LayerRecord& record);
void Failpoint(int phase);
}

class OpenUsdReviewState
{
public:
    struct Receipt
    {
        OpenUsdEdit::Identity identity;
        std::string reviewHash;
        std::string manifestHash;
        std::string targetIdentifier;
        std::string anchor;
    };
    OpenUsdReview::Source source;
    std::vector<std::shared_ptr<OpenUsdReview::Origin>> origins;
    std::vector<SdfLayerRefPtr> sourceLayers;
    ArResolverContext resolver;
    SdfLayerRefPtr session;
    TfRefPtr<OpenUsdEdit::CountedData> sessionData;
    uint64_t sessionVersion = 0;
    uint64_t userVersion = 0;
    std::string documentId;
    bool imported = false;
    std::map<std::string, Receipt> receipts;
};
