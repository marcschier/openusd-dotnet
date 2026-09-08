// Copyright (c) marcschier. Licensed under the MIT License.

#pragma once

#include "common.h"
#include "openusd_hierarchy.h"

static_assert(sizeof(openusd_hierarchy_limits) == 32);
static_assert(sizeof(openusd_hierarchy_entry) == 32);
static_assert(sizeof(openusd_hierarchy_variant_set) == 8);
static_assert(offsetof(openusd_hierarchy_view, change_serial) == 8);
static_assert(offsetof(openusd_hierarchy_view, entries) == 24);
static_assert(offsetof(openusd_hierarchy_entry, string_offset) == 16);

struct openusd_hierarchy_snapshot
{
    std::vector<openusd_hierarchy_entry> entries;
    std::vector<openusd_hierarchy_variant_set> variant_sets;
    std::vector<char> data;
    std::vector<uint32_t> offsets;
};

namespace OpenUsdHierarchy
{
struct Budget
{
    explicit Budget(const openusd_hierarchy_limits& value) : limits(value) {}

    openusd_hierarchy_limits limits;
    size_t text = 0;
    size_t work = 0;
    size_t variant_sets = 0;
    size_t variant_names = 0;
    size_t source_variant_sets = 0;
    size_t source_variant_names = 0;

    static void Check(const char* name, size_t observed, size_t limit)
    {
        if (observed > limit)
        {
            throw std::length_error(std::string("Hierarchy ") + name + " quota exceeded (limit " +
                std::to_string(limit) + ", observed at least " + std::to_string(observed) + ").");
        }
    }

    void Text(size_t count)
    {
        Check("text bytes", count + text, limits.maximum_text_bytes);
        text += count;
    }

    void Work(size_t count = 1)
    {
        Check("metadata work", count + work, limits.maximum_metadata_work);
        work += count;
    }
};

void Append(openusd_hierarchy_snapshot& result, Budget& budget, std::string_view value);

class VariantReader
{
public:
    explicit VariantReader(Budget& budget);
    ~VariantReader();
    void Read(const UsdPrim& prim, openusd_hierarchy_snapshot& result, openusd_hierarchy_entry& entry);

private:
    struct Impl;
    std::unique_ptr<Impl> _impl;
};
}
