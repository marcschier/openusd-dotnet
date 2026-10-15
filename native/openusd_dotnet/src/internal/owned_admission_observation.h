// Copyright (c) marcschier. Licensed under the MIT License.
#pragma once

#include <cstddef>

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <atomic>
#include <cerrno>
#include <cstring>
#endif

namespace OpenUsdOwnedAdmissionObservation
{
#if defined(_WIN32)
inline std::atomic<unsigned> records{0};
#endif

template <std::size_t Count>
inline void Write(const char (&phase)[Count]) noexcept
{
    static_assert(Count <= 32, "Admission observations must be bounded fixed labels.");
#if defined(_WIN32)
    const DWORD savedError = GetLastError();
    const int savedErrno = errno;
    char enabled[2]{};
    if (GetEnvironmentVariableA("OPENUSD_DOTNET_OWN_ADMISSION_PHASES", enabled, sizeof(enabled)) == 1 &&
        enabled[0] == '1' && records.load(std::memory_order_relaxed) < 64 &&
        records.fetch_add(1, std::memory_order_relaxed) < 64)
    {
        constexpr char prefix[] = "[OWN-admission-phase] ";
        char line[sizeof(prefix) - 1 + Count];
        std::memcpy(line, prefix, sizeof(prefix) - 1);
        std::memcpy(line + sizeof(prefix) - 1, phase, Count - 1);
        line[sizeof(line) - 1] = '\n';
        DWORD written = 0;
        WriteFile(GetStdHandle(STD_ERROR_HANDLE), line, static_cast<DWORD>(sizeof(line)), &written, nullptr);
    }
    errno = savedErrno;
    SetLastError(savedError);
#else
    static_cast<void>(phase);
#endif
}

class SpecificationCall final
{
public:
    SpecificationCall() noexcept { Write("get-specification.entry"); }
    ~SpecificationCall() { Write("get-specification.return"); }
};
}
