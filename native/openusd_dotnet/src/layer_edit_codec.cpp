// Copyright (c) marcschier. Licensed under the MIT License.
#include "internal/layer_edit.h"
#include "openusd_review_document.h"

struct openusd_edit_buffer
{
    std::vector<uint8_t> bytes;
};

namespace OpenUsdEdit
{
bool ValidUtf8(std::string_view value)
{
    for (size_t i = 0; i < value.size();)
    {
        const auto first = static_cast<uint8_t>(value[i++]);
        if (first == 0) { return false; }
        if (first < 0x80) { continue; }
        size_t count;
        uint32_t code;
        uint32_t minimum;
        if (first >= 0xc2 && first <= 0xdf) { count = 1; code = first & 0x1f; minimum = 0x80; }
        else if (first >= 0xe0 && first <= 0xef) { count = 2; code = first & 0x0f; minimum = 0x800; }
        else if (first >= 0xf0 && first <= 0xf4) { count = 3; code = first & 7; minimum = 0x10000; }
        else { return false; }
        if (count > value.size() - i) { return false; }
        while (count--)
        {
            const auto next = static_cast<uint8_t>(value[i++]);
            if ((next & 0xc0) != 0x80) { return false; }
            code = (code << 6) | (next & 0x3f);
        }
        if (code < minimum || code > 0x10ffff || (code >= 0xd800 && code <= 0xdfff)) { return false; }
    }
    return true;
}
static_assert(sizeof(int) == 4 && sizeof(float) == 4 && sizeof(double) == 8,
    "The editing wire requires 32-bit int/float and 64-bit double.");
static_assert(std::numeric_limits<float>::is_iec559 && std::numeric_limits<double>::is_iec559,
    "The editing wire requires IEEE-754 float and double.");

std::string InputText(const char* value, size_t size)
{
    Check(value && size > 0 && size <= MaxString, "Editing string must contain 1..4096 bytes.");
    Check(std::memchr(value, 0, size) == nullptr, "Editing strings cannot contain NUL.");
    return std::string(value, size);
}

void Writer::Need(size_t size) const
{
    Check(size <= MaxBytes - bytes.size(), "Editing packet byte budget exceeded (4 MiB).");
}

void Writer::U32(uint32_t value)
{
    Need(4);
    for (int i = 0; i < 4; ++i)
    {
        bytes.push_back(static_cast<uint8_t>(value >> (i * 8)));
    }
}

void Writer::U64(uint64_t value)
{
    Need(8);
    for (int i = 0; i < 8; ++i)
    {
        bytes.push_back(static_cast<uint8_t>(value >> (i * 8)));
    }
}

void Writer::F32(float value)
{
    uint32_t bits;
    std::memcpy(&bits, &value, 4);
    U32(bits);
}

void Writer::F64(double value)
{
    uint64_t bits;
    std::memcpy(&bits, &value, 8);
    U64(bits);
}

void Writer::Text(std::string_view value)
{
    Check(value.size() <= MaxString, "Editing string byte budget exceeded (4096).");
    Check(value.find('\0') == std::string_view::npos, "NUL in editing string.");
    Check(!portable || ValidUtf8(value), "Portable review strings require strict UTF-8 without NUL.");
    Need(4 + value.size());
    U32(static_cast<uint32_t>(value.size()));
    bytes.insert(bytes.end(), value.begin(), value.end());
}

void Writer::Header(uint32_t kind)
{
    U32(Magic);
    U32(1);
    U32(kind);
}

Reader::Reader(const uint8_t* bytes, size_t count, bool portableValues)
    : data(bytes), size(count), portable(portableValues)
{
    Check(data && size >= 12 && size <= MaxBytes, "Invalid editing packet size.");
}

void Reader::Need(size_t count) const
{
    Check(count <= size - offset, "Truncated editing packet.");
}

uint32_t Reader::U32()
{
    Need(4);
    uint32_t value = 0;
    for (int i = 0; i < 4; ++i)
    {
        value |= static_cast<uint32_t>(data[offset++]) << (i * 8);
    }
    return value;
}

uint64_t Reader::U64()
{
    Need(8);
    uint64_t value = 0;
    for (int i = 0; i < 8; ++i)
    {
        value |= static_cast<uint64_t>(data[offset++]) << (i * 8);
    }
    return value;
}

float Reader::F32()
{
    const uint32_t bits = U32();
    float value;
    std::memcpy(&value, &bits, 4);
    return value;
}

double Reader::F64()
{
    const uint64_t bits = U64();
    double value;
    std::memcpy(&value, &bits, 8);
    return value;
}

std::string Reader::Text()
{
    const size_t count = Count(MaxString);
    Need(count);
    const auto* start = reinterpret_cast<const char*>(data + offset);
    Check(std::memchr(start, 0, count) == nullptr, "NUL in editing string.");
    std::string value(start, count);
    Check(!portable || ValidUtf8(value), "Portable review strings require strict UTF-8 without NUL.");
    offset += count;
    return value;
}

uint32_t Reader::Count(size_t limit)
{
    const uint32_t count = U32();
    Check(count <= limit, "Editing item count budget exceeded.");
    return count;
}

void Reader::Header(uint32_t kind)
{
    Check(U32() == Magic && U32() == 1 && U32() == kind,
        "Unsupported editing packet magic, version, or kind.");
}

void Reader::End() const
{
    Check(offset == size, "Trailing bytes in editing packet.");
}

void Outputs(openusd_edit_buffer** owner, openusd_edit_buffer_view* view)
{
    ResetAbiOutput(owner);
    ResetAbiOutput(view);
    Check(owner && view, "Editing buffer owner and view outputs are required.");
}

void Publish(std::vector<uint8_t> bytes, openusd_edit_buffer** owner,
    openusd_edit_buffer_view* view)
{
    Check(bytes.size() <= MaxBytes, "Editing publication byte budget exceeded (4 MiB).");
    auto result = std::make_unique<openusd_edit_buffer>();
    result->bytes = std::move(bytes);
    view->data = result->bytes.data();
    view->size = result->bytes.size();
    *owner = result.release();
}

void PublishPortable(std::vector<uint8_t> bytes, openusd_edit_buffer** owner,
    openusd_edit_buffer_view* view)
{
    Check(bytes.size() <= OPENUSD_REVIEW_MAX_ENVELOPE_BYTES,
        "Portable review publication byte budget exceeded (24 MiB).");
    auto result = std::make_unique<openusd_edit_buffer>();
    result->bytes = std::move(bytes);
    view->data = result->bytes.data();
    view->size = result->bytes.size();
    *owner = result.release();
}
}

void openusd_edit_buffer_release(openusd_edit_buffer* buffer)
{
    delete buffer;
}
