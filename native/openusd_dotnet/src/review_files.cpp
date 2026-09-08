// Copyright (c) marcschier. Licensed under the MIT License.
#include "internal/review_document.h"

#include <filesystem>

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>
#endif

namespace OpenUsdReview
{
struct FileReadScope::Impl
{
    static thread_local Impl* current;
    Impl* previous;
#if defined(_WIN32)
    std::map<std::string, HANDLE> files;
#endif
    Impl() : previous(current) { current = this; }
    ~Impl()
    {
#if defined(_WIN32)
        for (const auto& file : files) { CloseHandle(file.second); }
#endif
        current = previous;
    }
};
thread_local FileReadScope::Impl* FileReadScope::Impl::current = nullptr;
FileReadScope::FileReadScope() : impl(std::make_unique<Impl>())
{
    Failpoint(5);
}
FileReadScope::~FileReadScope() = default;

void FileReadScope::Retain(const std::string& path)
{
    auto* scope = Impl::current;
    Check(scope != nullptr, "Verified source reads require a scoped filesystem lease.");
#if defined(_WIN32)
    if (scope->files.find(path) != scope->files.end()) { return; }
    Check(scope->files.size() < MaxFiles, "Filesystem read lease budget exceeded (1024 files).");
    const auto name = std::filesystem::u8path(path).wstring();
    HANDLE handle = CreateFileW(name.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
    if (handle == INVALID_HANDLE_VALUE)
    {
        throw Conflict("Cannot obtain a stable read lease for a source/dependency. Close external writers, reconcile file access, and retry OpenForReview/capture/import.");
    }
    try { scope->files.emplace(path, handle); }
    catch (...) { CloseHandle(handle); throw; }
#else
    (void)path;
    throw std::runtime_error("Verified source opening currently requires Windows stable-read leases. The URD1 codec is platform-neutral; ordinary Open remains available on this platform.");
#endif
}
}
