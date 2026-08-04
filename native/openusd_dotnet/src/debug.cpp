// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

namespace
{
bool TfDebugSymbolExists(const std::string& name)
{
    const std::vector<std::string> names = TfDebug::GetDebugSymbolNames();
    return std::find(names.begin(), names.end(), name) != names.end();
}
}

openusd_status openusd_tf_debug_get_symbol_names(
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetStringListOutput(list, view);
        if (list == nullptr || view == nullptr || view->struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(error, "A valid list output and versioned view are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return GuardStringListOutput(error, list, view, [&](auto& result)
        {
            result = std::make_unique<openusd_string_list>();
            FillStringList(result.get(), TfDebug::GetDebugSymbolNames(), view);
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_tf_debug_get_symbol_description(
    const char* name,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiStringOutput(buffer, capacity);
        ResetAbiOutput(required);
        if (name == nullptr || name[0] == '\0' || required == nullptr)
        {
            WriteError(error, "A debug symbol name and string-size output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        const std::string symbol(name);
        if (!TfDebugSymbolExists(symbol))
        {
            WriteError(error, std::string("Unknown TfDebug symbol: ") + symbol);
            return OPENUSD_STATUS_NOT_FOUND;
        }
        return CopyString(TfDebug::GetDebugSymbolDescription(symbol), buffer, capacity, required);
    });
}

openusd_status openusd_tf_debug_set_symbol(
    const char* name,
    int32_t enabled,
    int32_t* changed,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(changed);
        if (name == nullptr || name[0] == '\0' || changed == nullptr)
        {
            WriteError(error, "A debug symbol name and changed output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        const std::string symbol(name);
        const std::vector<std::string> matched =
            TfDebug::SetDebugSymbolsByName(symbol, enabled != 0);
        if (matched.empty())
        {
            WriteError(error, std::string("Unknown TfDebug symbol: ") + symbol);
            return OPENUSD_STATUS_NOT_FOUND;
        }

        *changed = 1;
        return OPENUSD_STATUS_OK;
    });
}

openusd_status openusd_tf_debug_get_symbol_enabled(
    const char* name,
    int32_t* enabled,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(enabled);
        if (name == nullptr || name[0] == '\0' || enabled == nullptr)
        {
            WriteError(error, "A debug symbol name and enabled output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        const std::string symbol(name);
        if (!TfDebugSymbolExists(symbol))
        {
            WriteError(error, std::string("Unknown TfDebug symbol: ") + symbol);
            return OPENUSD_STATUS_NOT_FOUND;
        }
        *enabled = TfDebug::IsDebugSymbolNameEnabled(symbol) ? 1 : 0;
        return OPENUSD_STATUS_OK;
    });
}
