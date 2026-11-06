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
    HdSilkPageLimitExceeded(size_t present, size_t additional, size_t limit, size_t largestMesh = 0) noexcept
    {
        const int length = std::snprintf(_message, sizeof(_message),
            "hdSilk command page refused %llu additional bytes after %llu bytes "
            "(limit %llu). No command page was published.",
            static_cast<unsigned long long>(additional), static_cast<unsigned long long>(present),
            static_cast<unsigned long long>(limit));
        if (largestMesh != 0 && length > 0 && static_cast<size_t>(length) < sizeof(_message))
        {
            std::snprintf(_message + length, sizeof(_message) - static_cast<size_t>(length),
                " Largest mesh command: %llu bytes.", static_cast<unsigned long long>(largestMesh));
        }
    }

    const char* what() const noexcept override { return _message; }

private:
    char _message[320]{};
};

class HdSilkCommandPayload;

class HdSilkCommandStorageError final : public std::exception
{
public:
    explicit HdSilkCommandStorageError(const char* message) noexcept
    {
        std::snprintf(_message, sizeof(_message), "%s", message);
    }
    const char* what() const noexcept override { return _message; }
private:
    char _message[256]{};
};

/// A private, unpublished command store. Append/rollback/flush failures abort publication.
class HdSilkCommandSink
{
public:
    virtual ~HdSilkCommandSink() = default;
    virtual void Append(const uint8_t* bytes, size_t count) = 0;
    virtual void Truncate(size_t size) = 0;
    virtual void Flush() = 0;
};

/// A bounded contiguous page or one reusable command buffer feeding a private
/// store. Allocator overhead and reallocation overlap are not serialized bytes.
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

    HdSilkPageWriter(size_t maximumBytes, HdSilkCommandSink& sink, size_t maximumCommandBytes)
        : _maximum(maximumBytes),
          _maximumCommand(std::min(maximumCommandBytes, _bytes.max_size())),
          _sink(&sink)
    {
        if (maximumBytes == 0 || maximumCommandBytes < 8)
        {
            throw std::invalid_argument("Positive update storage and an eight-byte command buffer are required.");
        }
    }

    HdSilkPageWriter(const HdSilkPageWriter&) = delete;
    HdSilkPageWriter& operator=(const HdSilkPageWriter&) = delete;

    size_t size() const noexcept { return _committed + _bytes.size(); }
    size_t capacity() const noexcept { return _bytes.capacity(); }

    void CheckAdditional(size_t additional, size_t largestCommand = 0) const
    {
        RequireStorage();
        if (additional > _maximum - size())
        {
            throw HdSilkPageLimitExceeded(size(), additional, _maximum, largestCommand);
        }
        if (_sink && largestCommand > _maximumCommand)
        {
            throw HdSilkPageLimitExceeded(0, largestCommand, _maximumCommand, largestCommand);
        }
    }

    void resize(size_t size)
    {
        if (_commandActive || size > this->size())
        {
            throw std::logic_error("Only completed hdSilk commands can be rolled back.");
        }
        if (_sink)
        {
            Store([&] { _sink->Truncate(size); });
            _committed = size;
            _bytes.clear();
        }
        else { _bytes.resize(size); }
    }

    void Finish()
    {
        if (_commandActive) { throw std::logic_error("An hdSilk command is still being written."); }
        RequireStorage();
        if (_sink)
        {
            Store([&] { _sink->Flush(); });
        }
    }

    std::vector<uint8_t> Take()
    {
        if (_commandActive) { throw std::logic_error("An hdSilk command is still being written."); }
        if (_sink) { throw std::logic_error("Streamed hdSilk commands do not have a contiguous byte buffer."); }
        return std::move(_bytes);
    }

private:
    friend class HdSilkCommandPayload;

    template<typename Operation>
    void Store(Operation operation)
    {
        RequireStorage();
        try { operation(); }
        catch (const std::bad_alloc&) { _failed = true; throw; }
        catch (const std::exception& error)
        {
            _failed = true;
            throw HdSilkCommandStorageError(error.what());
        }
        catch (...) { _failed = true; throw; }
    }

    void RequireStorage() const
    {
        if (_failed) { throw HdSilkCommandStorageError("The hdSilk command store previously failed."); }
    }

    void Require(size_t additional)
    {
        CheckAdditional(additional);
        if (_sink && additional > _maximumCommand - _bytes.size())
        {
            throw HdSilkPageLimitExceeded(_bytes.size(), additional, _maximumCommand);
        }
        Reserve(_bytes.size() + additional);
    }

    void Reserve(size_t required)
    {
        if (required > _bytes.capacity())
        {
            const size_t capacity = _bytes.capacity();
            const size_t maximum = _sink ? std::min(_maximum, _maximumCommand) : _maximum;
            const size_t grown = capacity > maximum / 2 ? maximum : capacity * 2;
            _bytes.reserve(std::max(required, grown));
        }
    }

    std::vector<uint8_t> _bytes;
    size_t _maximum;
    size_t _maximumCommand = 0;
    size_t _committed = 0;
    HdSilkCommandSink* _sink = nullptr;
    bool _failed = false;
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
            _page._bytes.resize(_page._sink ? 0 : _start);
            _page._commandActive = false;
        }
    }

    HdSilkCommandPayload(const HdSilkCommandPayload&) = delete;
    HdSilkCommandPayload& operator=(const HdSilkCommandPayload&) = delete;

    size_t size() const noexcept { return _page.size() - _start - 8; }

    void reserve(size_t hint)
    {
        RequireActive();
        size_t maximumPayload = std::min(
            _page._maximum - _start - 8,
            static_cast<size_t>(std::numeric_limits<uint32_t>::max()) - 8);
        if (_page._sink) { maximumPayload = std::min(maximumPayload, _page._maximumCommand - 8); }
        const size_t capacity = (_page._sink ? 0 : _start) + 8 + std::min(hint, maximumPayload);
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
        RequireActive();
        if (count == 0) { return; }
        ValidateGrowth(count);
        if (data == nullptr)
        {
            throw std::invalid_argument("A nonempty hdSilk byte payload requires a source buffer.");
        }
        _page.Require(count);
        const size_t offset = _page._bytes.size();
        _page._bytes.resize(offset + count);
        std::memcpy(_page._bytes.data() + offset, data, count);
    }

    uint8_t& operator[](size_t offset)
    {
        RequireActive();
        if (offset >= size()) { throw std::out_of_range("The hdSilk payload offset is outside its command."); }
        return _page._bytes[(_page._sink ? 0 : _start) + 8 + offset];
    }

    void Complete()
    {
        RequireActive();
        const auto byteSize = static_cast<uint32_t>(_page.size() - _start);
        for (size_t byte = 0; byte < 4; ++byte)
        {
            _page._bytes[(_page._sink ? 0 : _start) + 4 + byte] = static_cast<uint8_t>(byteSize >> (byte * 8));
        }
        _complete = true;
        _page._commandActive = false;
        if (_page._sink)
        {
            _page.Store([&] { _page._sink->Append(_page._bytes.data(), _page._bytes.size()); });
            _page._committed += _page._bytes.size();
            _page._bytes.clear();
        }
    }

private:
    void RequireActive() const
    {
        if (_complete) { throw std::logic_error("The hdSilk command is already complete."); }
        _page.RequireStorage();
    }

    void ValidateGrowth(size_t count) const
    {
        RequireActive();
        constexpr size_t maximumPayload = static_cast<size_t>(std::numeric_limits<uint32_t>::max()) - 8;
        const size_t present = size();
        if (present > maximumPayload || count > maximumPayload - present)
        {
            throw std::length_error("An hdSilk command exceeds the 32-bit byte_size field.");
        }
    }

    void Require(size_t count)
    {
        ValidateGrowth(count);
        _page.Require(count);
    }

    HdSilkPageWriter& _page;
    size_t _start;
    bool _complete = false;
};

#endif
