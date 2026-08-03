// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

#include "pxr/base/plug/plugin.h"
#include "pxr/usdValidation/usdValidation/context.h"
#include "pxr/usdValidation/usdValidation/registry.h"

struct openusd_validation_metadata_list
{
    std::vector<openusd_validation_metadata_record> records;
    std::vector<char> data;
    std::vector<size_t> offsets;
};

struct openusd_validation_error_list
{
    std::vector<openusd_validation_error_record> records;
    std::vector<char> data;
    std::vector<size_t> offsets;
};

namespace
{
template <typename TList>
void AppendString(TList& list, const std::string& value)
{
    if (value.find('\0') != std::string::npos)
    {
        throw std::invalid_argument("Validation strings must not contain embedded NULs.");
    }
    list.offsets.push_back(list.data.size());
    list.data.insert(list.data.end(), value.begin(), value.end());
    list.data.push_back('\0');
}

void ResetMetadata(openusd_validation_metadata_list** list, openusd_validation_metadata_view* view) noexcept
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
        view->version = OPENUSD_VALIDATION_METADATA_VIEW_VERSION;
    }
}

void ResetErrors(openusd_validation_error_list** list, openusd_validation_error_view* view) noexcept
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
        view->version = OPENUSD_VALIDATION_ERROR_VIEW_VERSION;
    }
}

openusd_status ValidateMetadataView(const openusd_validation_metadata_view* view, openusd_error_buffer* error)
{
    if (view == nullptr || !IsAligned(view) ||
        view->struct_size < sizeof(openusd_validation_metadata_view) ||
        view->version != OPENUSD_VALIDATION_METADATA_VIEW_VERSION)
    {
        WriteError(error, "A valid validation metadata view version 1 is required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    return OPENUSD_STATUS_OK;
}

openusd_status ValidateErrorView(const openusd_validation_error_view* view, openusd_error_buffer* error)
{
    if (view == nullptr || !IsAligned(view) ||
        view->struct_size < sizeof(openusd_validation_error_view) ||
        view->version != OPENUSD_VALIDATION_ERROR_VIEW_VERSION)
    {
        WriteError(error, "A valid validation error view version 1 is required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    return OPENUSD_STATUS_OK;
}

void FillMetadata(openusd_validation_metadata_list& list, openusd_validation_metadata_view* view)
{
    view->version = OPENUSD_VALIDATION_METADATA_VIEW_VERSION;
    view->records = list.records.empty() ? nullptr : list.records.data();
    view->records_size = list.records.size() * sizeof(openusd_validation_metadata_record);
    view->count = list.records.size();
    view->data = list.data.empty() ? nullptr : list.data.data();
    view->data_size = list.data.size();
    view->offsets = list.offsets.empty() ? nullptr : list.offsets.data();
    view->offsets_size = list.offsets.size() * sizeof(size_t);
    view->string_count = list.offsets.size();
}

void FillErrors(openusd_validation_error_list& list, openusd_validation_error_view* view)
{
    view->version = OPENUSD_VALIDATION_ERROR_VIEW_VERSION;
    view->records = list.records.empty() ? nullptr : list.records.data();
    view->records_size = list.records.size() * sizeof(openusd_validation_error_record);
    view->count = list.records.size();
    view->data = list.data.empty() ? nullptr : list.data.data();
    view->data_size = list.data.size();
    view->offsets = list.offsets.empty() ? nullptr : list.offsets.data();
    view->offsets_size = list.offsets.size() * sizeof(size_t);
    view->string_count = list.offsets.size();
}

std::string PluginName(const UsdValidationValidatorMetadata& metadata)
{
    return metadata.pluginPtr ? metadata.pluginPtr->GetName() : std::string();
}

std::string SiteString(const UsdValidationErrorSite& site)
{
    std::string layer;
    if (site.GetLayer())
    {
        layer = site.GetLayer()->GetIdentifier();
    }
    std::string path;
    UsdProperty property = site.GetProperty();
    if (property)
    {
        path = property.GetPath().GetString();
    }
    else
    {
        UsdPrim prim = site.GetPrim();
        if (prim)
        {
            path = prim.GetPath().GetString();
        }
        else if (site.GetPropertySpec())
        {
            path = site.GetPropertySpec()->GetPath().GetString();
        }
        else if (site.GetPrimSpec())
        {
            path = site.GetPrimSpec()->GetPath().GetString();
        }
    }
    return layer.empty() ? path : layer + "|" + path;
}

int32_t ConvertSeverity(UsdValidationErrorType type)
{
    switch (type)
    {
    case UsdValidationErrorType::None: return OPENUSD_VALIDATION_SEVERITY_NONE;
    case UsdValidationErrorType::Error: return OPENUSD_VALIDATION_SEVERITY_ERROR;
    case UsdValidationErrorType::Warn: return OPENUSD_VALIDATION_SEVERITY_WARNING;
    case UsdValidationErrorType::Info: return OPENUSD_VALIDATION_SEVERITY_INFO;
    default: return OPENUSD_VALIDATION_SEVERITY_ERROR;
    }
}

void AppendError(openusd_validation_error_list& list, const UsdValidationError& error)
{
    openusd_validation_error_record record{};
    record.severity = ConvertSeverity(error.GetType());
    record.string_offset = list.offsets.size();
    record.string_count = 3;
    const UsdValidationValidator* validator = error.GetValidator();
    AppendString(list, validator ? validator->GetMetadata().name.GetString() : std::string());
    AppendString(list, error.GetName().GetString());
    AppendString(list, error.GetMessage());
    record.site_offset = list.offsets.size();
    for (const UsdValidationErrorSite& site : error.GetSites())
    {
        AppendString(list, SiteString(site));
    }
    record.site_count = list.offsets.size() - record.site_offset;
    list.records.push_back(record);
}

openusd_status ReturnValidationErrors(
    UsdValidationErrorVector&& errors,
    openusd_validation_error_list** list,
    openusd_validation_error_view* view,
    openusd_error_buffer* error)
{
    std::unique_ptr<openusd_validation_error_list> result;
    const openusd_status status = Guard(error, [&]() -> openusd_status
    {
        result = std::make_unique<openusd_validation_error_list>();
        for (const UsdValidationError& validationError : errors)
        {
            if (!validationError.HasNoError())
            {
                AppendError(*result, validationError);
            }
        }
        FillErrors(*result, view);
        return OPENUSD_STATUS_OK;
    });
    if (status == OPENUSD_STATUS_OK && result)
    {
        *list = result.release();
    }
    else
    {
        ResetErrors(list, view);
    }
    return status;
}
}

void openusd_validation_metadata_list_release(openusd_validation_metadata_list* list)
{
    delete list;
}

void openusd_validation_error_list_release(openusd_validation_error_list* list)
{
    delete list;
}

openusd_status openusd_validation_get_registered_validators(
    openusd_validation_metadata_list** list,
    openusd_validation_metadata_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        const openusd_status viewStatus = ValidateMetadataView(view, error);
        ResetMetadata(list, view);
        // ABI_OUTPUT_INITIALIZATION
        if (list == nullptr || viewStatus != OPENUSD_STATUS_OK)
        {
            if (list == nullptr)
            {
                WriteError(error, "A validation metadata list output is required.");
            }
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        std::unique_ptr<openusd_validation_metadata_list> result;
        result = std::make_unique<openusd_validation_metadata_list>();
        UsdValidationRegistry& registry = UsdValidationRegistry::GetInstance();
        std::vector<const UsdValidationValidator*> validators = registry.GetOrLoadAllValidators();
        result->records.reserve(validators.size());
        for (const UsdValidationValidator* validator : validators)
        {
            if (validator == nullptr)
            {
                continue;
            }
            const UsdValidationValidatorMetadata& metadata = validator->GetMetadata();
            openusd_validation_metadata_record record{};
            record.is_suite = metadata.isSuite ? 1 : 0;
            record.is_time_dependent = metadata.isTimeDependent ? 1 : 0;
            record.string_offset = result->offsets.size();
            record.string_count = 3;
            AppendString(*result, metadata.name.GetString());
            AppendString(*result, metadata.doc);
            AppendString(*result, PluginName(metadata));
            record.keyword_offset = result->offsets.size();
            for (const TfToken& keyword : metadata.keywords)
            {
                AppendString(*result, keyword.GetString());
            }
            record.keyword_count = result->offsets.size() - record.keyword_offset;
            record.schema_type_offset = result->offsets.size();
            for (const TfToken& schemaType : metadata.schemaTypes)
            {
                AppendString(*result, schemaType.GetString());
            }
            record.schema_type_count = result->offsets.size() - record.schema_type_offset;
            result->records.push_back(record);
        }
        FillMetadata(*result, view);
        *list = result.release();
        return OPENUSD_STATUS_OK;
    });
}

openusd_status openusd_validation_validate_stage(
    const openusd_stage* stage,
    openusd_validation_error_list** list,
    openusd_validation_error_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        const openusd_status viewStatus = ValidateErrorView(view, error);
        ResetErrors(list, view);
        // ABI_OUTPUT_INITIALIZATION
        if (stage == nullptr || !stage->value || list == nullptr || viewStatus != OPENUSD_STATUS_OK)
        {
            WriteError(error, "A valid stage, validation error output, and error view version 1 are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        UsdValidationRegistry& registry = UsdValidationRegistry::GetInstance();
        UsdValidationContext context(registry.GetOrLoadAllValidators());
        return ReturnValidationErrors(context.Validate(stage->value), list, view, error);
    });
}

openusd_status openusd_validation_validate_prim(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_validation_error_list** list,
    openusd_validation_error_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        const openusd_status viewStatus = ValidateErrorView(view, error);
        ResetErrors(list, view);
        // ABI_OUTPUT_INITIALIZATION
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            list == nullptr || viewStatus != OPENUSD_STATUS_OK)
        {
            WriteError(error, "A valid stage, prim path, validation error output, and error view version 1 are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
        if (!prim)
        {
            WriteError(error, std::string("Prim was not found: ") + prim_path);
            return OPENUSD_STATUS_NOT_FOUND;
        }
        UsdValidationRegistry& registry = UsdValidationRegistry::GetInstance();
        UsdValidationContext context(registry.GetOrLoadAllValidators());
        std::vector<UsdPrim> prims{prim};
        return ReturnValidationErrors(context.Validate(prims), list, view, error);
    });
}
