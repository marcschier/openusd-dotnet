// Copyright (c) marcschier. Licensed under the MIT License.
#pragma once

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>

#include <array>
#include <atomic>
#include <cstdio>
#include <string>

namespace OpenUsdCreateLockTrace
{
inline std::atomic<size_t> Creates{0}, Saves{0}, LeaseScopes{0}, Leases{0};
inline std::atomic<size_t> CreateBegins{0}, CreateEnds{0}, SaveBegins{0}, SaveEnds{0};
inline std::atomic<size_t> LeaseOpens{0}, LeaseCloses{0}, LeaseCloseErrors{0}, Attempts{0};

struct WriteScope
{
    std::atomic<size_t>& active;
    std::atomic<size_t>& ends;
    WriteScope(std::atomic<size_t>& count, std::atomic<size_t>& begins, std::atomic<size_t>& completed)
        : active(count), ends(completed)
    {
        active.fetch_add(1, std::memory_order_relaxed);
        begins.fetch_add(1, std::memory_order_relaxed);
    }
    ~WriteScope()
    {
        ends.fetch_add(1, std::memory_order_relaxed);
        active.fetch_sub(1, std::memory_order_relaxed);
    }
};

inline bool Equal(const std::wstring& left, const std::wstring& right) noexcept
{
    return CompareStringOrdinal(left.c_str(), -1, right.c_str(), -1, TRUE) == CSTR_EQUAL;
}

inline std::wstring Utf16(const std::string& value)
{
    if (value.empty() || value.size() > 1024 || value.find('\0') != std::string::npos) { return {}; }
    const int count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS,
        value.data(), static_cast<int>(value.size()), nullptr, 0);
    if (count <= 0) { return {}; }
    std::wstring result(static_cast<size_t>(count), L'\0');
    if (MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(),
        static_cast<int>(value.size()), result.data(), count) != count) { return {}; }
    return result;
}

inline std::wstring FullPath(std::wstring value)
{
    if (value.size() < 3 || value.size() > 1024 || value[1] != L':' ||
        (value[2] != L'\\' && value[2] != L'/') ||
        value.find(L':', 2) != std::wstring::npos) { return {}; }
    for (wchar_t& character : value) { if (character == L'/') { character = L'\\'; } }
    std::array<wchar_t, 1025> buffer{};
    const DWORD count = GetFullPathNameW(value.c_str(), static_cast<DWORD>(buffer.size()), buffer.data(), nullptr);
    if (count == 0 || count >= buffer.size()) { return {}; }
    std::wstring normalized(buffer.data(), count);
    return Equal(value, normalized) ? normalized : std::wstring{};
}

inline bool Physical(const std::wstring& path, bool mayBeMissing)
{
    size_t component = 0;
    for (size_t end = 3; end <= path.size(); ++end)
    {
        if (end != path.size() && path[end] != L'\\') { continue; }
        if (++component > 32) { return false; }
        const DWORD attributes = GetFileAttributesW(path.substr(0, end).c_str());
        if (attributes == INVALID_FILE_ATTRIBUTES)
        {
            const DWORD error = GetLastError();
            return mayBeMissing && end == path.size() &&
                (error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND);
        }
        if ((attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0 ||
            (end < path.size() && (attributes & FILE_ATTRIBUTE_DIRECTORY) == 0)) { return false; }
    }
    return true;
}

inline void Refused(const char* reason) noexcept
{
    std::fprintf(stderr, "[DEBUG-openusd-create-lock] paths=refused reason=%s owner=not-established\n", reason);
}

inline void Failure(const char* target, const std::string& message, DWORD createReturnError) noexcept
{
    const DWORD previous = GetLastError();
    try
    {
        std::array<wchar_t, 2> enabled{};
        if (GetEnvironmentVariableW(L"OPENUSD_CREATE_LOCK_TRACE", enabled.data(), 2) != 1 ||
            enabled[0] != L'1' || Attempts.fetch_add(1, std::memory_order_relaxed) >= 4)
        {
            SetLastError(previous);
            return;
        }
        std::fprintf(stderr,
            "[DEBUG-openusd-create-lock] create-return-win32=%lu self-pid=%lu thread=%lu "
            "create-live=%zu create-begins=%zu create-ends=%zu save-live=%zu save-begins=%zu save-ends=%zu "
            "lease-scopes=%zu lease-live=%zu lease-opens=%zu lease-closes=%zu lease-close-errors=%zu "
            "sdk-write-handles=unobserved\n",
            static_cast<unsigned long>(createReturnError), static_cast<unsigned long>(GetCurrentProcessId()),
            static_cast<unsigned long>(GetCurrentThreadId()), Creates.load(), CreateBegins.load(), CreateEnds.load(),
            Saves.load(), SaveBegins.load(), SaveEnds.load(), LeaseScopes.load(), Leases.load(),
            LeaseOpens.load(), LeaseCloses.load(), LeaseCloseErrors.load());
        std::array<wchar_t, 1025> rootBuffer{};
        const DWORD rootLength = GetEnvironmentVariableW(
            L"OPENUSD_TEST_WORK_ROOT", rootBuffer.data(), static_cast<DWORD>(rootBuffer.size()));
        if (rootLength == 0 || rootLength >= rootBuffer.size() || !target || message.size() > 16384)
        {
            Refused("owned-root-or-message"); SetLastError(previous); return;
        }
        const std::wstring root = FullPath(std::wstring(rootBuffer.data(), rootLength));
        const std::wstring destination = FullPath(Utf16(target));
        const std::wstring rootPrefix = root + L"\\";
        const std::wstring anchor = L"\\artifacts\\native-managed-tests\\win-x64-";
        const size_t anchorAt = root.find(anchor);
        const size_t pidStart = anchorAt == std::wstring::npos ? root.size() : anchorAt + anchor.size();
        const size_t workAt = root.rfind(L"\\work");
        bool ownedPid = workAt != std::wstring::npos && pidStart < workAt && workAt + 5 == root.size();
        for (size_t index = pidStart; ownedPid && index < workAt; ++index)
        {
            ownedPid = root[index] >= L'0' && root[index] <= L'9';
        }
        const std::string prefix = "Failed to rename temporary file '";
        const size_t begin = message.find(prefix);
        const size_t separator = begin == std::string::npos ? begin : message.find("' to '", begin + prefix.size());
        const size_t finish = separator == std::string::npos ? separator : message.find("': ", separator + 6);
        if (!ownedPid || destination.size() <= rootPrefix.size() ||
            !Equal(destination.substr(0, rootPrefix.size()), rootPrefix) ||
            begin == std::string::npos || separator == std::string::npos || finish == std::string::npos ||
            message.find(prefix, begin + 1) != std::string::npos)
        {
            Refused("owned-rename-paths"); SetLastError(previous); return;
        }
        const auto sourceBytes = message.substr(begin + prefix.size(), separator - begin - prefix.size());
        const std::wstring source = FullPath(Utf16(sourceBytes));
        const std::wstring reported = FullPath(Utf16(message.substr(separator + 6, finish - separator - 6)));
        const size_t slash = destination.rfind(L'\\'), suffix = destination.rfind(L'.');
        if (!Equal(reported, destination) || suffix == std::wstring::npos || suffix <= slash ||
            source.size() != suffix + 7 || !Equal(source.substr(0, suffix + 1), destination.substr(0, suffix) + L".") ||
            !Physical(root, false) || !Physical(destination, true) || !Physical(source, false))
        {
            Refused("exact-sibling-or-physical-path"); SetLastError(previous); return;
        }
        for (size_t index = suffix + 1; index < source.size(); ++index)
        {
            const wchar_t value = source[index];
            if (!((value >= L'0' && value <= L'9') || (value >= L'A' && value <= L'Z') ||
                (value >= L'a' && value <= L'z')))
            {
                Refused("sdk-temp-name"); SetLastError(previous); return;
            }
        }
        const auto relativeHex = [&](const std::wstring& value)
        {
            const auto relative = value.substr(rootPrefix.size());
            std::array<char, 1025> bytes{};
            const int count = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, relative.data(),
                static_cast<int>(relative.size()), bytes.data(), static_cast<int>(bytes.size()), nullptr, nullptr);
            if (count <= 0) { return std::string{}; }
            const char* hex = "0123456789abcdef";
            std::string encoded;
            encoded.reserve(static_cast<size_t>(count) * 2);
            for (int index = 0; index < count; ++index)
            {
                const auto byte = static_cast<unsigned char>(bytes[static_cast<size_t>(index)]);
                encoded.push_back(hex[byte >> 4]); encoded.push_back(hex[byte & 15]);
            }
            return encoded;
        };
        const auto targetHex = relativeHex(destination), tempHex = relativeHex(source);
        if (targetHex.empty() || tempHex.empty()) { Refused("relative-encoding"); }
        else
        {
            std::fprintf(stderr, "[DEBUG-openusd-create-lock] owned-target-hex=%s owned-temp-hex=%s\n",
                targetHex.c_str(), tempHex.c_str());
        }
    }
    catch (...) { Refused("trace-exception-original-error-preserved"); }
    SetLastError(previous);
}
}
#endif
