// Copyright (c) marcschier. Licensed under the MIT License.
#ifndef HDSILK_PAGE_WRITER_H
#define HDSILK_PAGE_WRITER_H

#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <limits>
#include <new>
#include <stdexcept>
#include <utility>
#include <vector>

class HdSilkPageLimitExceeded final : public std::bad_alloc
{
public:
    HdSilkPageLimitExceeded(size_t present, size_t additional, size_t limit) noexcept
    {
        std::snprintf(_message, sizeof(_message),
            "hdSilk command page refused %llu additional bytes after %llu bytes "
            "(limit %llu). No command page was published.",
            static_cast<unsigned long long>(additional), static_cast<unsigned long long>(present),
            static_cast<unsigned long long>(limit));
    }

    const char* what() const noexcept override { return _message; }

private:
    char _message[224]{};
};

class HdSilkCommandPayload;

/// One serialized page buffer. Commands write into this buffer directly, and
/// every growth request is capped before allocation. Allocator overhead and
/// the old buffer temporarily held by vector reallocation are not page bytes.
class HdSilkPageWriter final
{
public:
    explicit HdSilkPageWriter(size_t maximumBytes)
        : _maximum(std::min(maximumBytes, _bytes.max_size()))
    {
        if (maximumBytes == 0)
        {
            throw std::invalid_argument("A positive hdSilk command page byte limit is required.");
        }
    }

    HdSilkPageWriter(const HdSilkPageWriter&) = delete;
    HdSilkPageWriter& operator=(const HdSilkPageWriter&) = delete;

    size_t size() const noexcept { return _bytes.size(); }
    size_t capacity() const noexcept { return _bytes.capacity(); }

    void resize(size_t size)
    {
        if (_commandActive || size > _bytes.size())
        {
            throw std::logic_error("Only completed hdSilk commands can be rolled back.");
        }
        _bytes.resize(size);
    }

    std::vector<uint8_t> Take()
    {
        if (_commandActive) { throw std::logic_error("An hdSilk command is still being written."); }
        return std::move(_bytes);
    }

private:
    friend class HdSilkCommandPayload;

    void Require(size_t additional)
    {
        if (additional > _maximum - _bytes.size())
        {
            throw HdSilkPageLimitExceeded(_bytes.size(), additional, _maximum);
        }
        Reserve(_bytes.size() + additional);
    }

    void Reserve(size_t required)
    {
        if (required > _bytes.capacity())
        {
            const size_t capacity = _bytes.capacity();
            const size_t grown = capacity > _maximum / 2 ? _maximum : capacity * 2;
            _bytes.reserve(std::max(required, grown));
        }
    }

    std::vector<uint8_t> _bytes;
    size_t _maximum;
    bool _commandActive = false;
};

/// A rollback-capable view of a command payload in the page's single buffer.
/// Its offsets exclude the command header, including for deformation hashes.
class HdSilkCommandPayload final
{
public:
    HdSilkCommandPayload(HdSilkPageWriter& page, uint32_t type)
        : _page(page), _start(page.size())
    {
        if (page._commandActive) { throw std::logic_error("Nested hdSilk commands are not supported."); }
        page.Require(8);
        for (size_t byte = 0; byte < 4; ++byte)
        {
            page._bytes.push_back(static_cast<uint8_t>(type >> (byte * 8)));
        }
        for (size_t byte = 0; byte < 4; ++byte) { page._bytes.push_back(0); }
        page._commandActive = true;
    }

    ~HdSilkCommandPayload()
    {
        if (!_complete)
        {
            _page._bytes.resize(_start);
            _page._commandActive = false;
        }
    }

    HdSilkCommandPayload(const HdSilkCommandPayload&) = delete;
    HdSilkCommandPayload& operator=(const HdSilkCommandPayload&) = delete;

    size_t size() const noexcept { return _page.size() - _start - 8; }

    void reserve(size_t hint)
    {
        const size_t maximumPayload = std::min(
            _page._maximum - _start - 8,
            static_cast<size_t>(std::numeric_limits<uint32_t>::max()) - 8);
        const size_t capacity = _start + 8 + std::min(hint, maximumPayload);
        _page.Reserve(capacity);
    }

    void AppendU32(uint32_t value)
    {
        Require(4);
        for (size_t byte = 0; byte < 4; ++byte)
        {
            _page._bytes.push_back(static_cast<uint8_t>(value >> (byte * 8)));
        }
    }

    void AppendU64(uint64_t value)
    {
        Require(8);
        for (size_t byte = 0; byte < 8; ++byte)
        {
            _page._bytes.push_back(static_cast<uint8_t>(value >> (byte * 8)));
        }
    }

    void AppendBytes(const void* data, size_t count)
    {
        if (count == 0) { return; }
        Require(count);
        const size_t offset = _page.size();
        _page._bytes.resize(offset + count);
        std::memcpy(_page._bytes.data() + offset, data, count);
    }

    uint8_t& operator[](size_t offset)
    {
        if (offset >= size()) { throw std::out_of_range("The hdSilk payload offset is outside its command."); }
        return _page._bytes[_start + 8 + offset];
    }

    void Complete() noexcept
    {
        const auto byteSize = static_cast<uint32_t>(_page.size() - _start);
        for (size_t byte = 0; byte < 4; ++byte)
        {
            _page._bytes[_start + 4 + byte] = static_cast<uint8_t>(byteSize >> (byte * 8));
        }
        _complete = true;
        _page._commandActive = false;
    }

private:
    void Require(size_t count)
    {
        constexpr size_t maximumPayload = static_cast<size_t>(std::numeric_limits<uint32_t>::max()) - 8;
        if (count > maximumPayload - size())
        {
            throw std::length_error("An hdSilk command exceeds the 32-bit byte_size field.");
        }
        _page.Require(count);
    }

    HdSilkPageWriter& _page;
    size_t _start;
    bool _complete = false;
};

#endif
