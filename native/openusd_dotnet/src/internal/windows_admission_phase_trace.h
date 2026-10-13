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
#include <cwchar>

namespace OpenUsdAdmissionTrace
{
inline std::atomic<size_t> Records{0};

inline bool Enabled() noexcept
{
    const DWORD original = GetLastError();
    std::array<wchar_t, 2> enabled{};
    if (GetEnvironmentVariableW(L"OPENUSD_ADMISSION_PHASE_TRACE", enabled.data(), 2) != 1 ||
        enabled[0] != L'1')
    {
        SetLastError(original);
        return false;
    }
    std::array<wchar_t, 1025> executable{};
    std::array<wchar_t, 1025> root{};
    const wchar_t* command = GetCommandLineW();
    const size_t count = command ? wcsnlen_s(command, 16384) : 0;
    const wchar_t* suffix = L"targets-deleted";
    const size_t suffixLength = 15;
    size_t end = count;
    if (end && command[end - 1] == L'"') { --end; }
    const DWORD length = GetModuleFileNameW(nullptr, executable.data(), static_cast<DWORD>(executable.size()));
    const wchar_t* name = wcsrchr(executable.data(), L'\\');
    const DWORD rootLength = GetEnvironmentVariableW(
        L"OPENUSD_ADMISSION_TRACE_BUILD_ROOT", root.data(), static_cast<DWORD>(root.size()));
    const bool result =
        length > 0 && length < executable.size() && name &&
        rootLength > 3 && rootLength < root.size() && length > rootLength &&
        executable[rootLength] == L'\\' &&
        _wcsnicmp(executable.data(), root.data(), rootLength) == 0 &&
        _wcsicmp(name + 1, L"openusd_render_admission_probe.exe") == 0 &&
        count < 16384 && end > suffixLength &&
        std::wmemcmp(command + end - suffixLength, suffix, suffixLength) == 0 &&
        (command[end - suffixLength - 1] == L' ' || command[end - suffixLength - 1] == L'"');
    SetLastError(original);
    return result;
}

inline void Phase(const char* phase, const char* state, ULONGLONG elapsed = 0) noexcept
{
    const DWORD original = GetLastError();
    if (Enabled() && Records.fetch_add(1, std::memory_order_relaxed) < 64)
    {
        std::fprintf(stderr,
            "[DEBUG-openusd-admission] pid=%lu thread=%lu phase=%s state=%s elapsed-ms=%llu tick-ms=%llu\n",
            static_cast<unsigned long>(GetCurrentProcessId()), static_cast<unsigned long>(GetCurrentThreadId()),
            phase, state, static_cast<unsigned long long>(elapsed),
            static_cast<unsigned long long>(GetTickCount64()));
        std::fflush(stderr);
    }
    SetLastError(original);
}

struct Scope
{
    const char* name;
    bool enabled;
    ULONGLONG began;
    explicit Scope(const char* phase) noexcept : name(phase), enabled(Enabled()),
        began(enabled ? GetTickCount64() : 0)
    {
        if (enabled) { Phase(name, "enter"); }
    }
    ~Scope()
    {
        if (enabled) { Phase(name, "exit", GetTickCount64() - began); }
    }
};

inline void Module(const char* role, HMODULE module) noexcept
{
    const DWORD original = GetLastError();
    std::array<wchar_t, 1025> path{};
    std::array<wchar_t, 1025> root{};
    const DWORD count = GetModuleFileNameW(module, path.data(), static_cast<DWORD>(path.size()));
    const DWORD rootLength = GetEnvironmentVariableW(
        L"OPENUSD_ADMISSION_TRACE_REPO_ROOT", root.data(), static_cast<DWORD>(root.size()));
    if (Enabled() && count > 0 && count < path.size() && rootLength > 3 && rootLength < root.size() &&
        count > rootLength && path[rootLength] == L'\\' &&
        _wcsnicmp(path.data(), root.data(), rootLength) == 0 && Records.fetch_add(1) < 64)
    {
        std::array<char, 4097> encoded{};
        const char* hex = "0123456789abcdef";
        for (size_t index = 0; index < count; ++index)
        {
            const auto value = static_cast<unsigned int>(path[index]);
            encoded[index * 4] = hex[(value >> 4) & 15];
            encoded[index * 4 + 1] = hex[value & 15];
            encoded[index * 4 + 2] = hex[(value >> 12) & 15];
            encoded[index * 4 + 3] = hex[(value >> 8) & 15];
        }
        std::fprintf(stderr, "[DEBUG-openusd-admission] module-role=%s module-path-utf16hex=%s\n",
            role, encoded.data());
        std::fflush(stderr);
    }
    SetLastError(original);
}

inline void Modules(const void* currentModuleAddress) noexcept
{
    if (!Enabled()) { return; }
    const DWORD original = GetLastError();
    HMODULE core = nullptr;
    if (GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(currentModuleAddress), &core))
    {
        Module("core", core);
    }
    Module("probe", nullptr);
    if (HMODULE sdk = GetModuleHandleW(L"usd_ms.dll")) { Module("sdk", sdk); }
    SetLastError(original);
}
}
#else
namespace OpenUsdAdmissionTrace
{
struct Scope { explicit Scope(const char*) noexcept {} };
inline void Phase(const char*, const char*) noexcept {}
inline void Modules(const void*) noexcept {}
}
#endif
