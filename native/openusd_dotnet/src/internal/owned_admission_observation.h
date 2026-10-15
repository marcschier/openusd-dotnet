// Copyright (c) marcschier. Licensed under the MIT License.
#pragma once

#include <cstddef>
#include <atomic>
#include <cerrno>
#include <cstdlib>
#include <cstring>

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#else
#include <unistd.h>
#endif

namespace OpenUsdOwnedAdmissionObservation
{
inline std::atomic<unsigned> records{0};

enum class Kind { Render, Property, StageRelease };

inline bool Enabled(const char* name) noexcept
{
#if defined(_WIN32)
    char value[2]{};
    return GetEnvironmentVariableA(name, value, sizeof(value)) == 1 && value[0] == '1';
#else
    const char* value = std::getenv(name);
    return value != nullptr && value[0] == '1' && value[1] == '\0';
#endif
}

template <Kind Observation = Kind::Render, std::size_t Count>
inline void Write(const char (&phase)[Count]) noexcept
{
    static_assert(Count <= 32, "Admission observations must be bounded fixed labels.");
#if defined(_WIN32)
    const DWORD savedError = GetLastError();
#endif
    const int savedErrno = errno;
    const bool enabled =
        (Observation != Kind::Property && Enabled("OPENUSD_DOTNET_OWN_ADMISSION_PHASES")) ||
        (Observation != Kind::Render && Enabled("OPENUSD_DOTNET_OWN_PROPERTY_PHASES"));
    if (enabled && records.load(std::memory_order_relaxed) < 64 &&
        records.fetch_add(1, std::memory_order_relaxed) < 64)
    {
        constexpr char prefix[] = "[OWN-admission-phase] ";
        char line[sizeof(prefix) - 1 + Count];
        std::memcpy(line, prefix, sizeof(prefix) - 1);
        std::memcpy(line + sizeof(prefix) - 1, phase, Count - 1);
        line[sizeof(line) - 1] = '\n';
#if defined(_WIN32)
        DWORD written = 0;
        WriteFile(GetStdHandle(STD_ERROR_HANDLE), line, static_cast<DWORD>(sizeof(line)), &written, nullptr);
#else
        static_cast<void>(::write(STDERR_FILENO, line, sizeof(line)));
#endif
    }
    errno = savedErrno;
#if defined(_WIN32)
    SetLastError(savedError);
#endif
}

class SpecificationCall final
{
public:
    SpecificationCall() noexcept { Write("get-specification.entry"); }
    ~SpecificationCall() { Write("get-specification.return"); }
};

class PropertyCall final
{
public:
    PropertyCall() noexcept { Write<Kind::Property>("property-snapshot.entry"); }
    ~PropertyCall() { Write<Kind::Property>("property-snapshot.return"); }
};
}
