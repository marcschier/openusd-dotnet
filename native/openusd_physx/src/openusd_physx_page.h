// Copyright (c) marcschier. Licensed under the MIT License.

// Strict validation and read-only access for the pointer free build page.
// This translation unit never includes PhysX or OpenUSD headers so the page
// contract can be compiled and tested without the simulation SDK.

#ifndef OPENUSD_PHYSX_PAGE_H
#define OPENUSD_PHYSX_PAGE_H

#include "openusd_physx_world.h"

#include <cstring>
#include <string_view>

namespace openusd_physx_page
{
// Read-only view over a page that has already passed Validate. Records are
// copied out by value so no alignment or aliasing assumption leaks to callers.
class View
{
public:
    View() noexcept = default;

    View(const unsigned char* base, size_t size, const openusd_physx_build_page_header& header) noexcept
        : base_(base)
        , size_(size)
        , header_(header)
    {
    }

    const openusd_physx_build_page_header& Header() const noexcept
    {
        return header_;
    }

    size_t Size() const noexcept
    {
        return size_;
    }

    bool IsEmpty() const noexcept
    {
        return base_ == nullptr;
    }

    template <typename TRecord>
    TRecord Get(const openusd_physx_page_span& span, size_t index) const noexcept
    {
        TRecord record{};
        std::memcpy(record_address(record), base_ + span.offset + index * sizeof(TRecord), sizeof(TRecord));
        return record;
    }

    std::string_view String(uint32_t offset, uint32_t length) const noexcept
    {
        if (base_ == nullptr || length == 0)
        {
            return std::string_view();
        }
        return std::string_view(
            reinterpret_cast<const char*>(base_) + header_.string_bytes.offset + offset,
            length);
    }

private:
    template <typename TRecord>
    static void* record_address(TRecord& record) noexcept
    {
        return static_cast<void*>(&record);
    }

    const unsigned char* base_ = nullptr;
    size_t size_ = 0;
    openusd_physx_build_page_header header_{};
};

// Validates every structural and semantic rule of the page contract. On
// success the optional view is filled; on failure validation carries the exact
// error code, section, element index, and byte offset, and error carries a
// human readable reason.
openusd_physx_status Validate(
    const void* page,
    size_t page_size,
    openusd_physx_page_validation* validation,
    View* view,
    openusd_physx_error_buffer* error);
}

#endif
