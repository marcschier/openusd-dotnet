// Copyright (c) marcschier. Licensed under the MIT License.
#ifndef HDSILK_COMMAND_FILE_H
#define HDSILK_COMMAND_FILE_H

#include "pageWriter.h"

#include <atomic>
#include <cerrno>
#include <filesystem>
#include <memory>
#include <random>
#include <string>

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#else
#include <fcntl.h>
#include <unistd.h>
#endif

class HdSilkCommandStorageBudget final
{
public:
    explicit HdSilkCommandStorageBudget(size_t maximumBytes) : _maximum(maximumBytes)
    {
        if (maximumBytes == 0) { throw std::invalid_argument("A positive live command-file budget is required."); }
    }

    size_t Maximum() const noexcept { return _maximum; }
    size_t Reserved() const noexcept { return _reserved.load(std::memory_order_relaxed); }
    size_t Peak() const noexcept { return _peak.load(std::memory_order_relaxed); }

private:
    friend class HdSilkCommandFile;

    void Reserve(size_t bytes)
    {
        size_t current = _reserved.load(std::memory_order_relaxed);
        for (;;)
        {
            if (bytes > _maximum - current)
            {
                throw HdSilkPageLimitExceeded(current, bytes, _maximum);
            }
            if (_reserved.compare_exchange_weak(current, current + bytes, std::memory_order_relaxed))
            {
                const size_t reserved = current + bytes;
                size_t peak = _peak.load(std::memory_order_relaxed);
                while (peak < reserved &&
                    !_peak.compare_exchange_weak(peak, reserved, std::memory_order_relaxed)) {}
                return;
            }
        }
    }

    void Release(size_t bytes) noexcept { _reserved.fetch_sub(bytes, std::memory_order_relaxed); }

    size_t _maximum;
    std::atomic<size_t> _reserved{0};
    std::atomic<size_t> _peak{0};
};

/// Private delete-on-close storage for one unpublished command update.
/// One caller serializes access. Reads are allowed only after successful flush.
class HdSilkCommandFile final : public HdSilkCommandSink
{
public:
    HdSilkCommandFile(size_t maximumBytes, size_t bufferBytes,
        std::shared_ptr<HdSilkCommandStorageBudget> budget = {})
        : _maximum(maximumBytes), _buffer(ValidateBuffer(maximumBytes, bufferBytes)), _budget(std::move(budget))
    {
        const auto directory = std::filesystem::temp_directory_path();
        std::random_device random;
        for (size_t attempt = 0; attempt < 16; ++attempt)
        {
            char name[80]{};
            std::snprintf(name, sizeof(name), "openusd-hdsilk-%08x%08x%08x%08x.tmp",
                static_cast<unsigned>(random()), static_cast<unsigned>(random()),
                static_cast<unsigned>(random()), static_cast<unsigned>(random()));
            const auto path = directory / name;
#if defined(_WIN32)
            _file = CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, CREATE_NEW,
                FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_DELETE_ON_CLOSE, nullptr);
            if (_file != INVALID_HANDLE_VALUE) { return; }
            const DWORD error = GetLastError();
            if (error != ERROR_FILE_EXISTS && error != ERROR_ALREADY_EXISTS) { Fail("create", error); }
#else
            static_assert(sizeof(off_t) >= 8, "Command storage requires 64-bit file offsets.");
            do { _file = open(path.c_str(), O_RDWR | O_CREAT | O_EXCL | O_CLOEXEC, 0600); }
            while (_file < 0 && errno == EINTR);
            if (_file < 0)
            {
                if (errno == EEXIST) { continue; }
                Fail("create", static_cast<unsigned long>(errno));
            }
            // No descriptor-inheritance window; unlink before any data write.
            if (unlink(path.c_str()) != 0)
            {
                const int error = errno;
                close(_file);
                _file = -1;
                Fail("unlink", static_cast<unsigned long>(error));
            }
            return;
#endif
        }
        throw HdSilkCommandStorageError("Could not create a unique private hdSilk command file.");
    }

    ~HdSilkCommandFile() override
    {
#if defined(_WIN32)
        if (_file != INVALID_HANDLE_VALUE) { CloseHandle(_file); }
#else
        if (_file >= 0) { close(_file); }
#endif
        if (_budget) { _budget->Release(_charged); }
    }

    HdSilkCommandFile(const HdSilkCommandFile&) = delete;
    HdSilkCommandFile& operator=(const HdSilkCommandFile&) = delete;

    size_t size() const noexcept { return _written + _buffered; }

    void Append(const uint8_t* bytes, size_t count) override
    {
        RequireWritable();
        if (count > _maximum - size()) { throw HdSilkPageLimitExceeded(size(), count, _maximum); }
        if (count != 0 && bytes == nullptr) { throw std::invalid_argument("Command bytes are required."); }
        if (_budget) { _budget->Reserve(count); }
        _charged += count;
        while (count != 0)
        {
            if (_buffered == 0 && count >= _buffer.size())
            {
                Write(bytes, _buffer.size());
                bytes += _buffer.size();
                count -= _buffer.size();
            }
            else
            {
                const size_t copy = std::min(count, _buffer.size() - _buffered);
                std::memcpy(_buffer.data() + _buffered, bytes, copy);
                _buffered += copy;
                bytes += copy;
                count -= copy;
                if (_buffered == _buffer.size()) { WriteBuffered(); }
            }
        }
    }

    void Truncate(size_t target) override
    {
        RequireWritable();
        if (target > size()) { throw std::invalid_argument("Command storage cannot grow through rollback."); }
        if (target >= _written)
        {
            _buffered = target - _written;
            ReleaseTo(target);
            return;
        }
        Seek(target);
#if defined(_WIN32)
        if (!SetEndOfFile(_file)) { Fail("truncate", GetLastError()); }
#else
        int result;
        do { result = ftruncate(_file, static_cast<off_t>(target)); } while (result < 0 && errno == EINTR);
        if (result < 0) { Fail("truncate", static_cast<unsigned long>(errno)); }
#endif
        _written = target;
        _buffered = 0;
        ReleaseTo(target);
    }

    void Flush() override
    {
        RequireWritable();
        WriteBuffered();
#if defined(_WIN32)
        if (!FlushFileBuffers(_file)) { Fail("flush", GetLastError()); }
#else
        int result;
        do { result = fsync(_file); } while (result < 0 && errno == EINTR);
        if (result < 0) { Fail("flush", static_cast<unsigned long>(errno)); }
#endif
        _finalized = true;
    }

    /// Copies one bounded chunk from a finalized store, without allocating.
    void Read(size_t offset, uint8_t* bytes, size_t count)
    {
        if (!_finalized || _failed) { throw HdSilkCommandStorageError("Command storage is not readable."); }
        if (offset > size() || count > size() - offset || count > _buffer.size() ||
            (count != 0 && bytes == nullptr))
        {
            throw std::invalid_argument("The command read exceeds its extent or bounded transfer buffer.");
        }
        if (count == 0) { return; }
        Seek(offset);
        while (count != 0)
        {
#if defined(_WIN32)
            DWORD copied = 0;
            if (!ReadFile(_file, bytes, static_cast<DWORD>(count), &copied, nullptr))
            {
                Fail("read", GetLastError());
            }
#else
            ssize_t copied;
            do { copied = read(_file, bytes, count); } while (copied < 0 && errno == EINTR);
            if (copied < 0) { Fail("read", static_cast<unsigned long>(errno)); }
#endif
            if (copied == 0) { throw HdSilkCommandStorageError("The command file ended before its recorded extent."); }
            const size_t received = static_cast<size_t>(copied);
            bytes += received;
            count -= received;
        }
    }

private:
    void ReleaseTo(size_t bytes) noexcept
    {
        if (_budget) { _budget->Release(_charged - bytes); }
        _charged = bytes;
    }

    static size_t ValidateBuffer(size_t maximumBytes, size_t bufferBytes)
    {
        if (maximumBytes == 0 || maximumBytes > static_cast<uint64_t>(std::numeric_limits<int64_t>::max()) ||
            bufferBytes == 0 || bufferBytes > std::numeric_limits<int32_t>::max())
        {
            throw std::invalid_argument("Positive bounded command storage and transfer sizes are required.");
        }
        return std::min(maximumBytes, bufferBytes);
    }

    void RequireWritable() const
    {
        if (_failed || _finalized) { throw HdSilkCommandStorageError("Command storage is not writable."); }
    }

    [[noreturn]] void Fail(const char* operation, unsigned long error)
    {
        _failed = _failed || !_finalized;
        char message[256]{};
        std::snprintf(message, sizeof(message),
            "hdSilk command storage %s failed (OS status %lu).", operation, error);
        throw HdSilkCommandStorageError(message);
    }

    void Seek(size_t offset)
    {
#if defined(_WIN32)
        LARGE_INTEGER position{};
        position.QuadPart = static_cast<LONGLONG>(offset);
        if (!SetFilePointerEx(_file, position, nullptr, FILE_BEGIN)) { Fail("seek", GetLastError()); }
#else
        if (lseek(_file, static_cast<off_t>(offset), SEEK_SET) < 0) { Fail("seek", static_cast<unsigned long>(errno)); }
#endif
    }

    void Write(const uint8_t* bytes, size_t count)
    {
        while (count != 0)
        {
#if defined(_WIN32)
            DWORD copied = 0;
            if (!WriteFile(_file, bytes, static_cast<DWORD>(count), &copied, nullptr))
            {
                Fail("write", GetLastError());
            }
#else
            ssize_t copied;
            do { copied = write(_file, bytes, count); } while (copied < 0 && errno == EINTR);
            if (copied < 0) { Fail("write", static_cast<unsigned long>(errno)); }
#endif
            if (copied == 0) { Fail("write made no progress", 0); }
            const size_t written = static_cast<size_t>(copied);
            _written += written;
            bytes += written;
            count -= written;
        }
    }

    void WriteBuffered()
    {
        Write(_buffer.data(), _buffered);
        _buffered = 0;
    }

    size_t _maximum;
    std::vector<uint8_t> _buffer;
    std::shared_ptr<HdSilkCommandStorageBudget> _budget;
    size_t _charged = 0;
    size_t _written = 0;
    size_t _buffered = 0;
    bool _failed = false;
    bool _finalized = false;
#if defined(_WIN32)
    HANDLE _file = INVALID_HANDLE_VALUE;
#else
    int _file = -1;
#endif
};

#endif
