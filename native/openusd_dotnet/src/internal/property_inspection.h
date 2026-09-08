// Copyright (c) marcschier. Licensed under the MIT License.

#pragma once

#include "common.h"
#include "openusd_property_inspection.h"

#include "pxr/usd/pcp/layerStack.h"
#include "pxr/usd/pcp/primIndex.h"

static_assert(sizeof(openusd_property_limits) == 40);
static_assert(sizeof(openusd_property_preview) == 24);
static_assert(sizeof(openusd_property_entry) == 128);
static_assert(sizeof(openusd_property_asset) == 8);
static_assert(offsetof(openusd_property_entry, value) == 48);
static_assert(offsetof(openusd_property_view, entries) == 40);

struct openusd_property_snapshot
{
    std::vector<openusd_property_entry> entries;
    std::vector<openusd_property_asset> assets;
    std::vector<double> times;
    std::vector<char> data;
    std::vector<uint32_t> offsets;
};

namespace OpenUsdProperties
{
struct Budget
{
    explicit Budget(const openusd_property_limits& value) : limits(value) {}
    openusd_property_limits limits;
    size_t text = 0;
    size_t work = 0;

    static void Check(const char* name, size_t count, size_t maximum)
    {
        if (count > maximum)
        {
            throw std::length_error(std::string("Property inspection ") + name +
                " quota exceeded (limit " + std::to_string(maximum) +
                ", observed at least " + std::to_string(count) + ").");
        }
    }

    void Text(size_t count)
    {
        Check("text bytes", text + count, limits.maximum_text_bytes);
        text += count;
    }

    void Work(size_t count = 1)
    {
        Check("metadata work", work + count, limits.maximum_metadata_work);
        work += count;
    }
};

uint32_t Append(openusd_property_snapshot& result, Budget& budget, std::string_view value);
bool PathText(const SdfPath& path, Budget& budget, std::string* text);
void Unavailable(openusd_property_preview& preview, uint32_t reason,
    uint32_t status = OPENUSD_PROPERTY_DEFERRED);
bool FixedValueType(const std::type_info& type);
void PreviewValue(const VtValue& value, const UsdStageRefPtr& stage,
    const SdfLayerRefPtr& layer, const PcpNodeRef& node,
    openusd_property_snapshot& result, Budget& budget, openusd_property_entry& entry);
void Build(const UsdPrim& prim, UsdTimeCode time,
    openusd_property_snapshot& result, Budget& budget);
}
