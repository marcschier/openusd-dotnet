// Copyright (c) marcschier. Licensed under the MIT License.
#include "internal/review_document.h"

#include <filesystem>
#include <random>

namespace OpenUsdReview
{
namespace
{
constexpr uint32_t BindingMagic = 0x31425352;
constexpr uint32_t EnvelopeMagic = 0x31454452;
constexpr uint32_t DocumentMagic = 0x31445255;
constexpr char Hex[] = "0123456789abcdef";

bool IsHash(const std::string& value)
{
    return value.size() == 64 && std::all_of(value.begin(), value.end(), [](char c)
    {
        return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
    });
}
void StoredPath(const std::string& text, bool inspectionOnly)
{
    CheckFilesystemPath(text);
    if (inspectionOnly)
    {
        const auto path = std::filesystem::u8path(text);
        Check(path.is_absolute() && path.lexically_normal().generic_u8string() == text,
            "Inspection provenance requires a lexical canonical absolute path.");
    }
    else
    {
        Check(Path(text, false) == text, "Portable path is not a canonical absolute filesystem path.");
    }
}
void WriteMetadata(PacketWriter& w, const Document& d)
{
    w.Text(d.id); w.Text(d.source.root); w.Text(d.source.fingerprint);
    w.Text(d.originalTarget); w.Text(d.target); w.Text(d.source.anchor);
    WriteFiles(w, d.source.files);
}
}

void PacketWriter::Raw(const uint8_t* data, size_t size)
{
    Check(bytes.size() <= limit && size <= limit - bytes.size(), "Portable review packet byte budget exceeded.");
    if (size) { Check(data != nullptr, "Missing portable bytes."); bytes.insert(bytes.end(), data, data + size); }
}
void PacketWriter::U32(uint32_t value)
{
    uint8_t data[4];
    for (size_t i = 0; i < 4; ++i) { data[i] = static_cast<uint8_t>(value >> (8 * i)); }
    Raw(data, 4);
}
void PacketWriter::U64(uint64_t value)
{
    U32(static_cast<uint32_t>(value)); U32(static_cast<uint32_t>(value >> 32));
}
void PacketWriter::Blob(const Bytes& value)
{
    Check(value.size() <= UINT32_MAX, "Portable blob size overflow.");
    U32(static_cast<uint32_t>(value.size())); Raw(value.data(), value.size());
}
void PacketWriter::Text(std::string_view value)
{
    Check(value.size() <= OpenUsdEdit::MaxString && OpenUsdEdit::ValidUtf8(value),
        "Portable text requires at most 4096 strict UTF-8 bytes without NUL.");
    U32(static_cast<uint32_t>(value.size()));
    Raw(reinterpret_cast<const uint8_t*>(value.data()), value.size());
}
PacketReader::PacketReader(const uint8_t* input, size_t count, size_t maximum)
    : data(input), size(count)
{
    Check(data && count >= 8 && count <= maximum, "Invalid portable packet size; inspect supported byte limits.");
}
void PacketReader::Need(size_t count) const { Check(count <= size - offset, "Truncated portable review packet."); }
uint32_t PacketReader::U32()
{
    Need(4);
    uint32_t value = 0;
    for (size_t i = 0; i < 4; ++i) { value |= static_cast<uint32_t>(data[offset++]) << (8 * i); }
    return value;
}
uint64_t PacketReader::U64()
{
    const auto low = U32();
    return low | (static_cast<uint64_t>(U32()) << 32);
}
uint32_t PacketReader::Count(size_t maximum)
{
    const auto value = U32();
    Check(value <= maximum, "Portable review count budget exceeded.");
    return value;
}
std::string PacketReader::Text()
{
    const auto count = Count(OpenUsdEdit::MaxString); Need(count);
    std::string result(reinterpret_cast<const char*>(data + offset), count);
    Check(OpenUsdEdit::ValidUtf8(result), "Portable text is not strict UTF-8 or contains NUL.");
    offset += count;
    return result;
}
Bytes PacketReader::Blob(size_t maximum)
{
    const auto count = Count(maximum); Need(count);
    Bytes result(data + offset, data + offset + count); offset += count; return result;
}
void PacketReader::End() const { Check(offset == size, "Trailing portable review bytes."); }

std::array<uint8_t, 32> Sha256(const uint8_t* bytes, size_t size)
{
    constexpr uint32_t k[64] = {
        0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
        0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
        0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
        0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
        0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
        0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
        0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
        0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2};
    uint32_t h[8] = {0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,
        0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19};
    const auto rotate = [](uint32_t n, unsigned bits) { return (n >> bits) | (n << (32 - bits)); };
    const auto block = [&](const uint8_t* data)
    {
        uint32_t w[64]{};
        for (size_t i = 0; i < 16; ++i)
        {
            for (size_t j = 0; j < 4; ++j) { w[i] = (w[i] << 8) | data[i * 4 + j]; }
        }
        for (size_t i = 16; i < 64; ++i)
        {
            const auto a = w[i - 15], b = w[i - 2];
            w[i] = w[i - 16] + (rotate(a, 7) ^ rotate(a, 18) ^ (a >> 3))
                + w[i - 7] + (rotate(b, 17) ^ rotate(b, 19) ^ (b >> 10));
        }
        uint32_t a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], n = h[7];
        for (size_t i = 0; i < 64; ++i)
        {
            const uint32_t first = n + (rotate(e, 6) ^ rotate(e, 11) ^ rotate(e, 25))
                + ((e & f) ^ (~e & g)) + k[i] + w[i];
            const uint32_t second = (rotate(a, 2) ^ rotate(a, 13) ^ rotate(a, 22))
                + ((a & b) ^ (a & c) ^ (b & c));
            n = g; g = f; f = e; e = d + first; d = c; c = b; b = a; a = first + second;
        }
        h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e; h[5] += f; h[6] += g; h[7] += n;
    };
    const size_t full = size / 64;
    for (size_t i = 0; i < full; ++i) { block(bytes + 64 * i); }
    uint8_t tail[128]{};
    const auto remaining = size % 64;
    if (remaining) { std::memcpy(tail, bytes + full * 64, remaining); }
    tail[remaining] = 0x80;
    const size_t finalSize = remaining < 56 ? 64 : 128;
    const uint64_t bits = static_cast<uint64_t>(size) * 8;
    for (size_t i = 0; i < 8; ++i) { tail[finalSize - 1 - i] = static_cast<uint8_t>(bits >> (8 * i)); }
    block(tail);
    if (finalSize == 128) { block(tail + 64); }
    std::array<uint8_t, 32> result{};
    for (size_t i = 0; i < 32; ++i) { result[i] = static_cast<uint8_t>(h[i / 4] >> (24 - 8 * (i % 4))); }
    return result;
}
std::string Hash(const uint8_t* bytes, size_t size)
{
    const auto hash = Sha256(bytes, size);
    std::string result;
    result.reserve(64);
    for (const auto b : hash) { result.push_back(Hex[b >> 4]); result.push_back(Hex[b & 15]); }
    return result;
}
std::string Guid()
{
    std::random_device random;
    std::array<uint8_t, 16> bytes{};
    for (auto& byte : bytes) { byte = static_cast<uint8_t>(random()); }
    bytes[6] = static_cast<uint8_t>((bytes[6] & 15) | 0x40);
    bytes[8] = static_cast<uint8_t>((bytes[8] & 63) | 0x80);
    std::string result;
    for (size_t i = 0; i < bytes.size(); ++i)
    {
        if (i == 4 || i == 6 || i == 8 || i == 10) { result.push_back('-'); }
        result.push_back(Hex[bytes[i] >> 4]); result.push_back(Hex[bytes[i] & 15]);
    }
    return result;
}
bool IsGuid(const std::string& text)
{
    if (text.size() != 36) { return false; }
    for (size_t i = 0; i < text.size(); ++i)
    {
        if (i == 8 || i == 13 || i == 18 || i == 23) { if (text[i] != '-') { return false; } }
        else if (!((text[i] >= '0' && text[i] <= '9') || (text[i] >= 'a' && text[i] <= 'f'))) { return false; }
    }
    return true;
}
std::string TextInput(const char* text, size_t size)
{
    const auto result = OpenUsdEdit::InputText(text, size);
    Check(OpenUsdEdit::ValidUtf8(result), "Portable input must be strict UTF-8 without NUL.");
    return result;
}
bool File::operator==(const File& other) const
{
    return path == other.path && hash == other.hash && length == other.length && kind == other.kind;
}
void WriteFiles(PacketWriter& writer, const std::vector<File>& files)
{
    Check(files.size() <= MaxFiles, "Portable manifest file budget exceeded (1024).");
    writer.U32(static_cast<uint32_t>(files.size()));
    for (const auto& file : files)
    {
        writer.Text(file.path); writer.Text(file.hash); writer.U64(file.length); writer.U32(file.kind);
    }
}
std::vector<File> ReadFiles(PacketReader& reader, bool inspectionOnly)
{
    const auto count = reader.Count(MaxFiles);
    reader.Need(static_cast<size_t>(count) * 20);
    std::vector<File> files;
    std::string previous;
    uint64_t total = 0;
    for (uint32_t i = 0; i < count; ++i)
    {
        File file{reader.Text(), reader.Text(), reader.U64(), reader.Count(1)};
        Check(file.path > previous && IsHash(file.hash) && file.length <= MaxFileBytes,
            "Manifest requires sorted unique canonical paths, SHA-256 and bounded file sizes.");
        StoredPath(file.path, inspectionOnly);
        total += file.length;
        Check(total <= MaxSourceBytes, "Manifest aggregate source byte budget exceeded (64 MiB).");
        previous = file.path;
        files.push_back(std::move(file));
    }
    return files;
}
void ValidateSource(const Source& source)
{
    Check(IsHash(source.fingerprint) && !source.root.empty() && Parent(source.root) == source.anchor,
        "Portable source fingerprint or anchor is invalid.");
    const auto found = std::find_if(source.files.begin(), source.files.end(), [&](const File& file)
    {
        return file.path == source.root && file.kind == 0 && file.hash == source.fingerprint;
    });
    Check(found != source.files.end(), "Manifest must contain the exact source-root byte image.");
}
Bytes BindingBytes(uint64_t stageId, const Source& source)
{
    PacketWriter writer(MaxEnvelope);
    writer.U32(BindingMagic); writer.U32(1); writer.U64(stageId);
    writer.Text(source.root); writer.Text(source.fingerprint); writer.Text(source.anchor);
    WriteFiles(writer, source.files);
    return std::move(writer.bytes);
}
bool MatchBinding(const openusd_stage* stage, const uint8_t* bytes, size_t size)
{
    PacketReader reader(bytes, size, MaxEnvelope);
    Check(reader.U32() == BindingMagic && reader.U32() == 1, "Unsupported source-binding magic or version.");
    const auto id = reader.U64();
    Source source;
    source.root = reader.Text(); source.fingerprint = reader.Text(); source.anchor = reader.Text();
    source.files = ReadFiles(reader); reader.End(); ValidateSource(source);
    const auto& context = EditContext(stage);
    Check(context.review != nullptr, "Unverified stage: close/reconcile it and use OpenForReview before any edits.");
    const auto& admitted = context.review->source;
    return id == context.id && source.root == admitted.root && source.fingerprint == admitted.fingerprint
        && source.anchor == admitted.anchor && source.files == admitted.files;
}
std::string ManifestHash(const std::vector<File>& files)
{
    PacketWriter w(MaxDocument); WriteFiles(w, files); return Hash(w.bytes);
}
Bytes EncodeDocument(const Document& document)
{
    ValidateSource(document.source);
    Check(IsGuid(document.id) && !document.originalTarget.empty() && !document.target.empty(),
        "Portable document identity/provenance is incomplete.");
    Check(document.review.size() <= MaxReview, "Portable review content exceeds 4 MiB.");
    PacketWriter body(MaxDocument - 48);
    WriteMetadata(body, document); body.Blob(document.review);
    const auto digest = Sha256(body.bytes.data(), body.bytes.size());
    PacketWriter writer(MaxDocument);
    writer.U32(DocumentMagic); writer.U32(1); writer.U32(static_cast<uint32_t>(body.bytes.size())); writer.U32(0);
    writer.Raw(digest.data(), digest.size()); writer.Raw(body.bytes.data(), body.bytes.size());
    return std::move(writer.bytes);
}
Document DecodeDocument(const uint8_t* bytes, size_t size, bool inspectionOnly)
{
    PacketReader reader(bytes, size, MaxDocument);
    Check(reader.U32() == DocumentMagic && reader.U32() == 1, "Unsupported URD1 document magic or version.");
    const auto payloadLength = reader.Count(MaxDocument - 48);
    Check(reader.U32() == 0 && size >= 48 && payloadLength == size - 48, "Invalid URD1 length or flags.");
    reader.Need(32);
    const auto digest = Sha256(bytes + 48, size - 48);
    Check(std::memcmp(bytes + 16, digest.data(), 32) == 0, "URD1 checksum mismatch; restore an intact review document.");
    reader.offset = 48;
    Document d;
    d.id = reader.Text(); d.source.root = reader.Text(); d.source.fingerprint = reader.Text();
    d.originalTarget = reader.Text(); d.target = reader.Text(); d.source.anchor = reader.Text();
    d.source.files = ReadFiles(reader, inspectionOnly);
    d.review = reader.Blob(MaxReview); reader.End(); ValidateSource(d.source);
    Check(IsGuid(d.id) && !d.originalTarget.empty(),
        "Invalid portable document identity or target provenance.");
    StoredPath(d.target, inspectionOnly);
    DecodeReview(d.review.data(), d.review.size(), inspectionOnly);
    return d;
}
Bytes Envelope(const Document& document, const Bytes& portable, const Bytes& receipt)
{
    PacketWriter writer(MaxEnvelope);
    writer.U32(EnvelopeMagic); writer.U32(1); writer.Blob(portable); writer.Blob(receipt);
    WriteMetadata(writer, document);
    return std::move(writer.bytes);
}
Bytes Inspection(const Document& document, size_t documentSize)
{
    Check(documentSize <= MaxDocument, "Inspection document byte budget exceeded.");
    PacketWriter writer(MaxEnvelope);
    writer.U32(0x31494452); writer.U32(1); writer.U32(static_cast<uint32_t>(documentSize));
    WriteMetadata(writer, document);
    return std::move(writer.bytes);
}
}
