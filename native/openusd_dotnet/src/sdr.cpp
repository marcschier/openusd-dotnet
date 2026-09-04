// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

#include "pxr/usd/sdf/assetPath.h"
#include "pxr/usd/sdr/registry.h"
#include "pxr/usd/sdr/shaderNode.h"
#include "pxr/usd/sdr/shaderProperty.h"

struct openusd_sdr_node_definition_list
{
    std::vector<openusd_sdr_node_definition_record> records;
    std::vector<openusd_sdr_property_record> properties;
    std::vector<char> data;
    std::vector<size_t> offsets;
};

namespace
{
constexpr size_t kMaxNodes = OPENUSD_SDR_NODE_DEFINITION_MAX_NODES;
constexpr size_t kMaxProperties = OPENUSD_SDR_NODE_DEFINITION_MAX_PROPERTIES;
constexpr size_t kMaxStringBytes = OPENUSD_SDR_NODE_DEFINITION_MAX_STRING_BYTES;

void AppendString(openusd_sdr_node_definition_list& list, const std::string& value)
{
    if (value.find('\0') != std::string::npos)
    {
        throw std::invalid_argument("Shader node-definition strings must not contain embedded NULs.");
    }
    list.offsets.push_back(list.data.size());
    list.data.insert(list.data.end(), value.begin(), value.end());
    list.data.push_back('\0');
}

// The exact byte count `value` occupies once packed with its terminating NUL. Pure and
// dependency-free so it is directly testable at arbitrary (including test-only, injected) sizes.
size_t EncodedByteCount(const std::string& value)
{
    return value.size() + 1;
}

size_t TotalEncodedBytes(const std::string* values, size_t count)
{
    size_t total = 0;
    for (size_t index = 0; index < count; ++index)
    {
        total += EncodedByteCount(values[index]);
    }
    return total;
}

// True if and only if appending `additional_bytes` more to a table that already holds
// `current_bytes` stays within `max_bytes`. Written to never overflow regardless of how large
// `additional_bytes` is (a single authored string can be arbitrarily long), so a caller can
// preflight one record's total encoded size before writing any of it.
bool HasStringCapacity(size_t current_bytes, size_t additional_bytes, size_t max_bytes)
{
    return additional_bytes <= max_bytes && current_bytes <= max_bytes - additional_bytes;
}

void ResetView(openusd_sdr_node_definition_list** list, openusd_sdr_node_definition_view* view) noexcept
{
    if (list != nullptr)
    {
        *list = nullptr;
    }
    if (view != nullptr)
    {
        const uint32_t structSize = view->struct_size;
        std::memset(view, 0, sizeof(*view));
        view->struct_size = structSize;
        view->version = OPENUSD_SDR_NODE_DEFINITION_VIEW_VERSION;
    }
}

openusd_status ValidateView(const openusd_sdr_node_definition_view* view, openusd_error_buffer* error)
{
    if (view == nullptr || !IsAligned(view) ||
        view->struct_size < sizeof(openusd_sdr_node_definition_view) ||
        view->version != OPENUSD_SDR_NODE_DEFINITION_VIEW_VERSION)
    {
        WriteError(error, "A valid shader node-definition view version 1 is required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    return OPENUSD_STATUS_OK;
}

void FillView(openusd_sdr_node_definition_list& list, uint32_t flags, openusd_sdr_node_definition_view* view)
{
    view->version = OPENUSD_SDR_NODE_DEFINITION_VIEW_VERSION;
    view->flags = flags;
    view->records = list.records.empty() ? nullptr : list.records.data();
    view->records_size = list.records.size() * sizeof(openusd_sdr_node_definition_record);
    view->record_count = list.records.size();
    view->properties = list.properties.empty() ? nullptr : list.properties.data();
    view->properties_size = list.properties.size() * sizeof(openusd_sdr_property_record);
    view->property_count = list.properties.size();
    view->data = list.data.empty() ? nullptr : list.data.data();
    view->data_size = list.data.size();
    view->offsets = list.offsets.empty() ? nullptr : list.offsets.data();
    view->offsets_size = list.offsets.size() * sizeof(size_t);
    view->string_count = list.offsets.size();
}

// Appends one shader property (input or output) to the shared string/property tables.
// Returns false, and sets truncated, once a capacity bound is reached; the caller stops
// adding further properties for the current node but keeps everything already recorded.
//
// The two encoded strings for this property are measured before anything is written, so a
// property whose own name/type cannot fit inside the remaining string budget is rejected as a
// whole: it never partially appends one string and then fails on the second.
bool AppendProperty(openusd_sdr_node_definition_list& list, SdrShaderPropertyConstPtr property, bool& truncated)
{
    if (property == nullptr)
    {
        return true;
    }
    if (list.properties.size() >= kMaxProperties)
    {
        truncated = true;
        return false;
    }

    const std::string values[2] = {
        property->GetName().GetString(),
        property->GetType().GetString()
    };
    if (!HasStringCapacity(list.data.size(), TotalEncodedBytes(values, 2), kMaxStringBytes))
    {
        truncated = true;
        return false;
    }

    openusd_sdr_property_record record{};
    record.direction = property->IsOutput()
        ? OPENUSD_SDR_PROPERTY_OUTPUT
        : OPENUSD_SDR_PROPERTY_INPUT;
    record.is_array = property->IsArray() ? 1 : 0;
    record.is_connectable = property->IsConnectable() ? 1 : 0;
    record.string_offset = list.offsets.size();
    record.string_count = 2;
    AppendString(list, values[0]);
    AppendString(list, values[1]);
    list.properties.push_back(record);
    return true;
}

// Appends one shader node definition, including its bounded input/output properties, to
// the list. Returns false, and sets truncated, once the node capacity bound is reached.
//
// The node's eight fixed strings are measured before anything is written, so a node whose own
// identity strings cannot fit inside the remaining string budget is rejected as a whole rather
// than publishing a record with some, but not all, of its fixed fields appended.
bool AppendNode(openusd_sdr_node_definition_list& list, SdrShaderNodeConstPtr node, bool& truncated)
{
    if (node == nullptr)
    {
        return true;
    }
    if (list.records.size() >= kMaxNodes)
    {
        truncated = true;
        return false;
    }

    const std::string values[8] = {
        node->GetIdentifier().GetString(),
        node->GetName(),
        node->GetFunction().GetString(),
        node->GetShadingSystem().GetString(),
        node->GetMetadataObject().GetContext().GetString(),
        node->GetResolvedDefinitionURI(),
        node->GetResolvedImplementationURI(),
        node->GetImplementationName()
    };
    if (!HasStringCapacity(list.data.size(), TotalEncodedBytes(values, 8), kMaxStringBytes))
    {
        truncated = true;
        return false;
    }

    openusd_sdr_node_definition_record record{};
    record.is_valid = node->IsValid() ? 1 : 0;
    record.string_offset = list.offsets.size();
    record.string_count = 8;
    for (const std::string& value : values)
    {
        AppendString(list, value);
    }

    record.property_offset = list.properties.size();
    for (const TfToken& inputName : node->GetShaderInputNames())
    {
        if (!AppendProperty(list, node->GetShaderInput(inputName), truncated))
        {
            break;
        }
    }
    if (!truncated)
    {
        for (const TfToken& outputName : node->GetShaderOutputNames())
        {
            if (!AppendProperty(list, node->GetShaderOutput(outputName), truncated))
            {
                break;
            }
        }
    }
    record.property_count = list.properties.size() - record.property_offset;
    list.records.push_back(record);
    return true;
}
}

void openusd_sdr_node_definition_list_release(openusd_sdr_node_definition_list* list)
{
    delete list;
}

openusd_status openusd_sdr_get_node_definitions(
    openusd_sdr_node_definition_list** list,
    openusd_sdr_node_definition_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        const openusd_status viewStatus = ValidateView(view, error);
        ResetView(list, view);
        // ABI_OUTPUT_INITIALIZATION
        if (list == nullptr || viewStatus != OPENUSD_STATUS_OK)
        {
            if (list == nullptr)
            {
                WriteError(error, "A shader node-definition list output is required.");
            }
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        auto result = std::make_unique<openusd_sdr_node_definition_list>();
        SdrRegistry& registry = SdrRegistry::GetInstance();
        bool truncated = false;
        for (SdrShaderNodeConstPtr node : registry.GetAllShaderNodes())
        {
            if (!AppendNode(*result, node, truncated))
            {
                break;
            }
        }

        FillView(
            *result,
            truncated
                ? static_cast<uint32_t>(OPENUSD_SDR_NODE_DEFINITION_FLAG_TRUNCATED)
                : 0u,
            view);
        *list = result.release();
        return OPENUSD_STATUS_OK;
    });
}

openusd_status openusd_sdr_get_node_definition_from_asset(
    const char* source_asset,
    const char* sub_identifier,
    const char* shading_system,
    openusd_sdr_node_definition_list** list,
    openusd_sdr_node_definition_view* view,
    int32_t* found,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        const openusd_status viewStatus = ValidateView(view, error);
        ResetView(list, view);
        ResetAbiOutput(found);
        // ABI_OUTPUT_INITIALIZATION
        if (list == nullptr || found == nullptr || source_asset == nullptr || source_asset[0] == '\0' ||
            viewStatus != OPENUSD_STATUS_OK)
        {
            WriteError(
                error,
                "A non-empty source asset, a found output, and a shader node-definition list and "
                "view version 1 are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        auto result = std::make_unique<openusd_sdr_node_definition_list>();
        SdrRegistry& registry = SdrRegistry::GetInstance();
        const TfToken subIdentifierToken(sub_identifier != nullptr ? sub_identifier : "");
        const TfToken shadingSystemToken(shading_system != nullptr ? shading_system : "");

        // A parser plugin that cannot resolve or parse the asset -- for example, an MDL
        // asset with no MDL SDK parser plugin registered -- is reported as "not found", not
        // as a native error. TfErrorMark inside Guard would otherwise turn the parser's own
        // diagnostics into a hard failure for a case this ABI documents as expected.
        TfErrorMark mark;
        SdrShaderNodeConstPtr node = registry.GetShaderNodeFromAsset(
            SdfAssetPath(std::string(source_asset)),
            SdrTokenMap(),
            subIdentifierToken,
            shadingSystemToken);
        mark.Clear();

        bool truncated = false;
        if (node != nullptr)
        {
            AppendNode(*result, node, truncated);
            *found = 1;
        }

        FillView(
            *result,
            truncated
                ? static_cast<uint32_t>(OPENUSD_SDR_NODE_DEFINITION_FLAG_TRUNCATED)
                : 0u,
            view);
        *list = result.release();
        return OPENUSD_STATUS_OK;
    });
}

#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
// Test-only: exposes the pure capacity predicate at arbitrary, small, injected sizes so its
// overflow-safe arithmetic can be proven directly rather than only by registering enough real
// Sdr nodes/properties to approach OPENUSD_SDR_NODE_DEFINITION_MAX_STRING_BYTES (64 MiB).
extern "C" OPENUSD_DOTNET_API int32_t openusd_test_sdr_has_string_capacity(
    size_t current_bytes,
    size_t additional_bytes,
    size_t max_bytes)
{
    return HasStringCapacity(current_bytes, additional_bytes, max_bytes) ? 1 : 0;
}

// Test-only: exercises the exact atomic-preflight append path AppendNode/AppendProperty use,
// against a scratch list and an injected byte budget, proving that a two-string record whose
// combined encoded size does not fit is rejected as a whole -- neither string is written, and
// `data_size_after` reports the table completely unchanged -- rather than partially appended.
extern "C" OPENUSD_DOTNET_API int32_t openusd_test_sdr_try_append_two_strings(
    const char* first_value,
    const char* second_value,
    size_t existing_data_bytes,
    size_t max_string_bytes,
    size_t* data_size_after)
{
    openusd_sdr_node_definition_list list;
    list.data.assign(existing_data_bytes, 'x');

    const std::string values[2] = {
        first_value != nullptr ? first_value : "",
        second_value != nullptr ? second_value : ""
    };
    int32_t appended = 0;
    if (HasStringCapacity(list.data.size(), TotalEncodedBytes(values, 2), max_string_bytes))
    {
        AppendString(list, values[0]);
        AppendString(list, values[1]);
        appended = 1;
    }

    if (data_size_after != nullptr)
    {
        *data_size_after = list.data.size();
    }
    return appended;
}
#endif
