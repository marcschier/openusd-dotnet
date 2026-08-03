// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

openusd_status openusd_stage_create_relationship(
    const openusd_stage* stage,
    const char* prim_path,
    const char* relationship_name,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            relationship_name == nullptr || relationship_name[0] == '\0')
        {
            WriteError(error, "A valid stage, prim path, and relationship name are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const UsdRelationship relationship = prim.CreateRelationship(TfToken(relationship_name), true);
            if (!relationship || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not create the relationship." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_set_relationship_targets(
    const openusd_stage* stage,
    const char* prim_path,
    const char* relationship_name,
    const openusd_string_list_view* targets,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            relationship_name == nullptr || relationship_name[0] == '\0' || targets == nullptr ||
            targets->struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(
                error,
                "A valid stage, prim path, relationship name, and versioned target list are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const openusd_status listValidation =
                ValidateStringListView(targets, "relationship-target list", error);
            if (listValidation != OPENUSD_STATUS_OK)
            {
                return listValidation;
            }
            TfErrorMark mark;
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }

            UsdRelationship relationship = prim.GetRelationship(TfToken(relationship_name));
            if (!relationship)
            {
                relationship = prim.CreateRelationship(TfToken(relationship_name), true);
            }
            if (!relationship)
            {
                WriteError(error, "The requested relationship was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const std::vector<SdfPath> paths = ReadPathList(targets);
            const bool set = relationship.SetTargets(SdfPathVector(paths.begin(), paths.end()));
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the relationship targets." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_relationship_targets(
    const openusd_stage* stage,
    const char* prim_path,
    const char* relationship_name,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetStringListOutput(list, view);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            relationship_name == nullptr || list == nullptr || view == nullptr ||
            view->struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(
                error,
                "A valid stage, prim path, relationship name, list output, and versioned view are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return GuardStringListOutput(error, list, view, [&](auto& result)
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            const UsdRelationship relationship =
                prim ? prim.GetRelationship(TfToken(relationship_name)) : UsdRelationship();
            if (!relationship)
            {
                WriteError(error, "The requested relationship was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            TfErrorMark mark;
            SdfPathVector targets;
            relationship.GetTargets(&targets);
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not read the relationship targets." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }

            std::vector<std::string> values;
            values.reserve(targets.size());
            for (const SdfPath& target : targets)
            {
                values.push_back(target.GetString());
            }

            result = std::make_unique<openusd_string_list>();
            FillStringList(result.get(), values, view);
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_clear_relationship_targets(
    const openusd_stage* stage,
    const char* prim_path,
    const char* relationship_name,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            relationship_name == nullptr || relationship_name[0] == '\0')
        {
            WriteError(error, "A valid stage, prim path, and relationship name are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            const UsdRelationship relationship =
                prim ? prim.GetRelationship(TfToken(relationship_name)) : UsdRelationship();
            if (!relationship)
            {
                WriteError(error, "The requested relationship was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const bool cleared = relationship.ClearTargets(false);
            if (!cleared || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not clear the relationship targets." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}
