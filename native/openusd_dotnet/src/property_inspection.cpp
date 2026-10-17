// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/property_inspection.h"
#include "pxr/base/tf/unicodeUtils.h"

namespace OpenUsdProperties
{
uint32_t Append(openusd_property_snapshot& result, Budget& budget, std::string_view value)
{
    budget.Text(value.size() + 1);
    if (value.find('\0') != std::string_view::npos)
    {
        throw std::invalid_argument("Property inspection text contains an embedded NUL.");
    }
    TfUtf8CodePointIterator iterator(value.begin(), value.end());
    while (iterator != TfUtf8CodePointIterator::PastTheEndSentinel{})
    {
        const auto start = iterator.GetBase();
        const auto codePoint = *iterator;
        ++iterator;
        if (codePoint == TfUtf8InvalidCodePoint &&
            value.substr(static_cast<size_t>(start - value.begin()),
                static_cast<size_t>(iterator.GetBase() - start)) != "\xef\xbf\xbd")
        {
            throw std::invalid_argument("Property inspection text is not valid UTF-8.");
        }
    }
    const auto index = static_cast<uint32_t>(result.offsets.size());
    result.offsets.push_back(static_cast<uint32_t>(result.data.size()));
    result.data.insert(result.data.end(), value.begin(), value.end());
    result.data.push_back('\0');
    return index;
}

bool PathText(const SdfPath& path, Budget& budget, std::string* text)
{
    size_t length = 1;
    for (const auto& prefix : path.GetAncestorsRange())
    {
        budget.Work();
        if (!prefix.IsPrimPath() && !prefix.IsPrimPropertyPath())
        {
            return false;
        }
        length += prefix.GetNameToken().GetString().size() + 1;
        Budget::Check("text bytes", budget.text + length, budget.limits.maximum_text_bytes);
    }
    // The path cache is permitted to allocate only after every borrowed name
    // and separator was admitted. Variant/target-expression paths are deferred.
    budget.Text(length);
    *text = path.GetString();
    return true;
}

void Unavailable(openusd_property_preview& preview, uint32_t reason, uint32_t status)
{
    preview.total_count = OPENUSD_PROPERTY_UNKNOWN_COUNT;
    preview.count = 0;
    preview.status = status;
    preview.reason = reason;
}
}

namespace
{
openusd_property_limits DefaultLimits()
{
    return {sizeof(openusd_property_limits), OPENUSD_PROPERTY_INSPECTION_VERSION,
        4096, 1024u * 1024u, 16, 16, 16, 262144, 4096, 0};
}

bool ValidLimits(const openusd_property_limits& value)
{
    return value.struct_size == sizeof(value) && value.version == OPENUSD_PROPERTY_INSPECTION_VERSION &&
        value.maximum_property_count <= OPENUSD_PROPERTY_MAX_PROPERTIES &&
        value.maximum_text_bytes <= OPENUSD_PROPERTY_MAX_TEXT_BYTES &&
        value.preview_elements <= OPENUSD_PROPERTY_MAX_PREVIEW_ELEMENTS &&
        value.time_sample_preview <= OPENUSD_PROPERTY_MAX_PREVIEW_ELEMENTS &&
        value.target_preview <= OPENUSD_PROPERTY_MAX_PREVIEW_ELEMENTS &&
        value.maximum_metadata_work <= OPENUSD_PROPERTY_MAX_METADATA_WORK &&
        value.maximum_preview_text_bytes <= OPENUSD_PROPERTY_MAX_PREVIEW_TEXT_BYTES && value.reserved == 0;
}
}

openusd_status openusd_stage_get_prim_property_snapshot(
    const openusd_stage* stage, const char* prim_path, int32_t time_sampled, double time_code,
    const openusd_property_limits* limits, openusd_property_snapshot** snapshot,
    openusd_property_view* view, openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        const bool validView = view != nullptr && IsAligned(view) &&
            view->struct_size == sizeof(*view) && view->version == OPENUSD_PROPERTY_INSPECTION_VERSION;
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(snapshot);
        ResetVersionedAbiOutput(view);
        const auto effective = limits == nullptr ? DefaultLimits() :
            (IsAligned(limits) && limits->struct_size == sizeof(*limits) &&
             limits->version == OPENUSD_PROPERTY_INSPECTION_VERSION ? *limits : openusd_property_limits{});
        if (!validView || snapshot == nullptr || !IsAligned(snapshot) || !ValidLimits(effective) ||
            prim_path == nullptr || (time_sampled != 0 && time_sampled != 1) ||
            !std::isfinite(time_code))
        {
            WriteError(error, "Aligned property outputs, bounded version 1 limits, a prim path and finite time are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        OpenUsdProperties::Budget budget(effective);
        size_t pathLength = 0;
        while (pathLength <= effective.maximum_text_bytes && prim_path[pathLength] != '\0') ++pathLength;
        budget.Text(pathLength + 1);
        if (!IsValidPrimPath(prim_path))
        {
            WriteError(error, "Property inspection requires an absolute prim path.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        std::unique_ptr<openusd_property_snapshot> result;
        openusd_property_view output{};
        const auto status = GuardStage(stage, error, [&]() -> openusd_status
        {
            if (stage == nullptr || !stage->value)
            {
                WriteError(error, "A valid stage is required for property inspection.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim || prim.IsPseudoRoot())
            {
                WriteError(error, "The requested inspection prim was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            result = std::make_unique<openusd_property_snapshot>();
            OpenUsdProperties::Append(*result, budget, std::string_view(prim_path, pathLength));
            OpenUsdProperties::Build(prim, GetTimeCode(time_sampled, time_code), *result, budget);
            output.struct_size = sizeof(output);
            output.version = OPENUSD_PROPERTY_INSPECTION_VERSION;
            output.change_serial = stage->change_serial.load(std::memory_order_relaxed);
            output.is_complete = std::all_of(result->entries.begin(), result->entries.end(), [](const auto& entry)
            {
                return entry.value.status == OPENUSD_PROPERTY_COMPLETE &&
                    entry.time_samples.status == OPENUSD_PROPERTY_COMPLETE &&
                    entry.targets.status == OPENUSD_PROPERTY_COMPLETE;
            }) ? 1u : 0u;
            output.metadata_work = static_cast<uint32_t>(budget.work);
            output.time_sampled = static_cast<uint32_t>(time_sampled);
            output.time_code = time_sampled ? time_code : 0;
            output.entries = result->entries.data();
            output.entries_size = result->entries.size() * sizeof(openusd_property_entry);
            output.entry_count = result->entries.size();
            output.assets = result->assets.data();
            output.assets_size = result->assets.size() * sizeof(openusd_property_asset);
            output.asset_count = result->assets.size();
            output.times = result->times.data();
            output.times_size = result->times.size() * sizeof(double);
            output.time_count = result->times.size();
            output.data = result->data.data();
            output.data_size = result->data.size();
            output.offsets = result->offsets.data();
            output.offsets_size = result->offsets.size() * sizeof(uint32_t);
            output.string_count = result->offsets.size();
            return OPENUSD_STATUS_OK;
        });
        if (status == OPENUSD_STATUS_OK)
        {
            *view = output;
            *snapshot = result.release();
        }
        return status;
    });
}

void openusd_property_snapshot_release(openusd_property_snapshot* snapshot)
{
    delete snapshot;
}
