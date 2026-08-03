// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

openusd_status openusd_stage_add_reference(
    const openusd_stage* stage,
    const char* prim_path,
    const char* asset_path,
    const char* target_prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            asset_path == nullptr || asset_path[0] == '\0')
        {
            WriteError(error, "A valid stage, prim path, and asset path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (target_prim_path != nullptr && target_prim_path[0] != '\0' &&
            !IsValidPrimPath(target_prim_path))
        {
            WriteError(error, "The target prim path must be an absolute prim path.");
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

            const SdfPath targetPath =
                (target_prim_path != nullptr && target_prim_path[0] != '\0')
                    ? SdfPath(target_prim_path)
                    : SdfPath();
            const SdfReference reference(asset_path, targetPath);
            const bool added = prim.GetReferences().AddReference(reference);
            if (!added || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not add the reference." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_clear_references(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage and absolute prim path are required.");
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

            const bool cleared = prim.GetReferences().ClearReferences();
            if (!cleared || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not clear the references." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_add_payload(
    const openusd_stage* stage,
    const char* prim_path,
    const char* asset_path,
    const char* target_prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            asset_path == nullptr || asset_path[0] == '\0')
        {
            WriteError(error, "A valid stage, prim path, and asset path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (target_prim_path != nullptr && target_prim_path[0] != '\0' &&
            !IsValidPrimPath(target_prim_path))
        {
            WriteError(error, "The target prim path must be an absolute prim path.");
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

            const SdfPath targetPath =
                (target_prim_path != nullptr && target_prim_path[0] != '\0')
                    ? SdfPath(target_prim_path)
                    : SdfPath();
            const SdfPayload payload(asset_path, targetPath);
            const bool added = prim.GetPayloads().AddPayload(payload);
            if (!added || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not add the payload." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_clear_payloads(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage and absolute prim path are required.");
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

            const bool cleared = prim.GetPayloads().ClearPayloads();
            if (!cleared || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not clear the payloads." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_composed_payload_arcs(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_payload_arc_list** list,
    openusd_payload_arc_list_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        uint32_t struct_size = 0;
        uint32_t requested_version = 0;
        if (view != nullptr)
        {
            std::memcpy(&struct_size, view, sizeof(struct_size));
            if (struct_size >=
                offsetof(openusd_payload_arc_list_view, version) + sizeof(uint32_t))
            {
                std::memcpy(
                    &requested_version,
                    reinterpret_cast<const unsigned char*>(view) +
                        offsetof(openusd_payload_arc_list_view, version),
                    sizeof(requested_version));
            }
        }

        // ABI_OUTPUT_INITIALIZATION
        ResetPayloadArcListOutput(list, view);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            list == nullptr || view == nullptr || !IsAligned(view) ||
            struct_size < sizeof(openusd_payload_arc_list_view) ||
            requested_version != OPENUSD_PAYLOAD_ARC_LIST_VIEW_VERSION)
        {
            WriteError(
                error,
                "A valid stage, prim path, list output, and aligned payload-arc view "
                "version 1 are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return GuardPayloadArcListOutput(error, list, view, [&](auto& result)
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (IsCompositionEnumerationFailpoint("payload-arcs"))
            {
                TF_RUNTIME_ERROR("Injected payload-arc composition diagnostic.");
                return OPENUSD_STATUS_OK;
            }

            std::vector<PayloadArcValue> values;
            const PcpPrimIndex prim_index = prim.ComputeExpandedPrimIndex();
            for (const PcpNodeRef& node : prim_index.GetNodeRange())
            {
                if (node.IsDueToAncestor())
                {
                    continue;
                }

                SdfPayloadVector payloads;
                PcpArcInfoVector arc_info;
                PcpErrorVector composition_errors;
                PcpComposeSitePayloads(
                    node,
                    &payloads,
                    &arc_info,
                    nullptr,
                    &composition_errors);
                if (!composition_errors.empty())
                {
                    std::string message;
                    for (const PcpErrorBasePtr& composition_error : composition_errors)
                    {
                        if (composition_error)
                        {
                            if (!message.empty())
                            {
                                message.push_back('\n');
                            }
                            message.append(composition_error->ToString());
                        }
                    }
                    WriteError(
                        error,
                        message.empty()
                            ? "Could not compose the direct payload list."
                            : message);
                    return OPENUSD_STATUS_NATIVE_ERROR;
                }
                if (payloads.size() != arc_info.size())
                {
                    WriteError(error, "OpenUSD returned mismatched payload and source metadata.");
                    return OPENUSD_STATUS_NATIVE_ERROR;
                }
                for (size_t index = 0; index < payloads.size(); ++index)
                {
                    if (!arc_info[index].sourceLayer)
                    {
                        WriteError(error, "A composed payload entry has no source layer.");
                        return OPENUSD_STATUS_NATIVE_ERROR;
                    }
                    values.push_back(
                        PayloadArcValue{
                            arc_info[index].authoredAssetPath,
                            payloads[index].GetPrimPath().GetString(),
                            arc_info[index].sourceLayer->GetIdentifier()});
                }
            }

            result = std::make_unique<openusd_payload_arc_list>();
            FillPayloadArcList(result.get(), values, view);
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_add_inherit(
    const openusd_stage* stage,
    const char* prim_path,
    const char* inherited_prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            !IsValidPrimPath(inherited_prim_path))
        {
            WriteError(error, "A valid stage and two absolute prim paths are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (!stage->value->GetPrimAtPath(SdfPath(inherited_prim_path)))
            {
                WriteError(error, std::string("Inherited prim was not found: ") + inherited_prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const bool added = prim.GetInherits().AddInherit(SdfPath(inherited_prim_path));
            if (!added || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not add the inherit arc." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_clear_inherits(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage and absolute prim path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const bool cleared = prim.GetInherits().ClearInherits();
            if (!cleared || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not clear the inherit arcs." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_add_specialize(
    const openusd_stage* stage,
    const char* prim_path,
    const char* specialized_prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            !IsValidPrimPath(specialized_prim_path))
        {
            WriteError(error, "A valid stage and two absolute prim paths are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (!stage->value->GetPrimAtPath(SdfPath(specialized_prim_path)))
            {
                WriteError(error, std::string("Specialized prim was not found: ") + specialized_prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const bool added = prim.GetSpecializes().AddSpecialize(SdfPath(specialized_prim_path));
            if (!added || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not add the specialize arc." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_clear_specializes(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage and absolute prim path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const bool cleared = prim.GetSpecializes().ClearSpecializes();
            if (!cleared || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not clear the specialize arcs." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_load_prim(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage and absolute prim path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (prim.IsInPrototype())
            {
                WriteError(error, "Prototype prims cannot be loaded directly.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }

            TfErrorMark mark;
            prim.Load();
            if (!mark.IsClean() || !prim.IsLoaded())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not load the prim." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_unload_prim(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage and absolute prim path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (prim.IsInPrototype())
            {
                WriteError(error, "Prototype prims cannot be unloaded directly.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }

            TfErrorMark mark;
            prim.Unload();
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not unload the prim." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_is_prim_loaded(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* loaded,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(loaded);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) || loaded == nullptr)
        {
            WriteError(error, "A valid stage, absolute prim path, and result output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
        if (!prim)
        {
            WriteError(error, std::string("Prim was not found: ") + prim_path);
            return OPENUSD_STATUS_NOT_FOUND;
        }
        *loaded = prim.IsLoaded() ? 1 : 0;
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_stage_set_instanceable(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t instanceable,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage and absolute prim path are required.");
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

            const bool set = prim.SetInstanceable(instanceable != 0);
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the instanceable state." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_instanceable(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* instanceable,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(instanceable);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) || instanceable == nullptr)
        {
            WriteError(error, "A valid stage, prim path, and output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }
            *instanceable = prim.IsInstanceable() ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_is_prim_instance(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* instance,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(instance);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) || instance == nullptr)
        {
            WriteError(error, "A valid stage, absolute prim path, and result output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
        if (!prim)
        {
            WriteError(error, std::string("Prim was not found: ") + prim_path);
            return OPENUSD_STATUS_NOT_FOUND;
        }
        *instance = prim.IsInstance() ? 1 : 0;
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_stage_is_prim_prototype(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* prototype,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(prototype);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) || prototype == nullptr)
        {
            WriteError(error, "A valid stage, absolute prim path, and result output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
        if (!prim)
        {
            WriteError(error, std::string("Prim was not found: ") + prim_path);
            return OPENUSD_STATUS_NOT_FOUND;
        }
        *prototype = prim.IsPrototype() ? 1 : 0;
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_stage_get_prim_prototype_path(
    const openusd_stage* stage,
    const char* prim_path,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiStringOutput(buffer, capacity);
        ResetAbiOutput(required);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage and absolute prim path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (!prim.IsInstance())
            {
                WriteError(error, "Only instance prims have a prototype path.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }

            const UsdPrim prototype = prim.GetPrototype();
            if (!prototype || !prototype.IsPrototype())
            {
                WriteError(error, "The instance has no valid prototype prim.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return CopyString(prototype.GetPath().GetString(), buffer, capacity, required);
        });

    });
}

openusd_status openusd_stage_add_variant_set(
    const openusd_stage* stage,
    const char* prim_path,
    const char* variant_set_name,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            variant_set_name == nullptr || variant_set_name[0] == '\0')
        {
            WriteError(error, "A valid stage, prim path, and variant set name are required.");
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

            const UsdVariantSet variantSet = prim.GetVariantSets().AddVariantSet(variant_set_name);
            if (!variantSet.IsValid() || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not add the variant set." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_variant_set_names(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        uint32_t struct_size = 0;
        if (view != nullptr)
        {
            std::memcpy(&struct_size, view, sizeof(struct_size));
        }

        // ABI_OUTPUT_INITIALIZATION
        ResetStringListOutput(list, view);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            list == nullptr || view == nullptr || !IsAligned(view) ||
            struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(
                error,
                "A valid stage, prim path, list output, and aligned versioned view are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return GuardStringListOutput(error, list, view, [&](auto& result)
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (IsCompositionEnumerationFailpoint("variant-set-names"))
            {
                TF_RUNTIME_ERROR("Injected variant-set composition diagnostic.");
                return OPENUSD_STATUS_OK;
            }

            const std::vector<std::string> names = prim.GetVariantSets().GetNames();
            result = std::make_unique<openusd_string_list>();
            FillStringList(result.get(), names, view);
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_add_variant(
    const openusd_stage* stage,
    const char* prim_path,
    const char* variant_set_name,
    const char* variant_name,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            variant_set_name == nullptr || variant_set_name[0] == '\0' ||
            variant_name == nullptr || variant_name[0] == '\0')
        {
            WriteError(error, "A valid stage, prim path, variant set name, and variant name are required.");
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

            UsdVariantSet variantSet = prim.GetVariantSets().GetVariantSet(variant_set_name);
            const bool added = variantSet.IsValid() && variantSet.AddVariant(variant_name);
            if (!added || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not add the variant." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_set_variant_selection(
    const openusd_stage* stage,
    const char* prim_path,
    const char* variant_set_name,
    const char* variant_selection,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            variant_set_name == nullptr || variant_set_name[0] == '\0')
        {
            WriteError(error, "A valid stage, prim path, and variant set name are required.");
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

            if (!prim.GetVariantSets().HasVariantSet(variant_set_name))
            {
                WriteError(error, "The requested variant set was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            UsdVariantSet variantSet = prim.GetVariantSets().GetVariantSet(variant_set_name);
            const bool set = (variant_selection != nullptr && variant_selection[0] != '\0')
                ? variantSet.SetVariantSelection(variant_selection)
                : variantSet.ClearVariantSelection();
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the variant selection." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_variant_selection(
    const openusd_stage* stage,
    const char* prim_path,
    const char* variant_set_name,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiStringOutput(buffer, capacity);
        ResetAbiOutput(required);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            variant_set_name == nullptr || required == nullptr)
        {
            WriteError(error, "A valid stage, prim path, variant set name, and size output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (!prim.GetVariantSets().HasVariantSet(variant_set_name))
            {
                WriteError(error, "The requested variant set was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const UsdVariantSet variantSet = prim.GetVariantSets().GetVariantSet(variant_set_name);
            std::string selection;
            if (!variantSet.HasAuthoredVariantSelection(&selection))
            {
                WriteError(error, "The variant set has no authored selection.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            return CopyString(selection, buffer, capacity, required);
        });

    });
}

openusd_status openusd_stage_get_variant_names(
    const openusd_stage* stage,
    const char* prim_path,
    const char* variant_set_name,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        uint32_t struct_size = 0;
        if (view != nullptr)
        {
            std::memcpy(&struct_size, view, sizeof(struct_size));
        }

        // ABI_OUTPUT_INITIALIZATION
        ResetStringListOutput(list, view);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            variant_set_name == nullptr || list == nullptr || view == nullptr ||
            !IsAligned(view) || struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(
                error,
                "A valid stage, prim path, variant set name, list output, and aligned versioned view "
                "are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return GuardStringListOutput(error, list, view, [&](auto& result)
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (!prim.GetVariantSets().HasVariantSet(variant_set_name))
            {
                WriteError(error, "The requested variant set was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const UsdVariantSet variantSet = prim.GetVariantSets().GetVariantSet(variant_set_name);
            const std::vector<std::string> names = variantSet.GetVariantNames();
            result = std::make_unique<openusd_string_list>();
            FillStringList(result.get(), names, view);
            return OPENUSD_STATUS_OK;
        });

    });
}
