// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

namespace
{
template <typename TSchema>
bool IsSchema(const UsdPrim& prim)
{
    return static_cast<bool>(TSchema(prim));
}

template <typename TSchema>
openusd_status DefineSchema(
    const openusd_stage* stage,
    const char* prim_path,
    const char* schema_name,
    openusd_error_buffer* error)
{
    TfErrorMark mark;
    const TSchema schema = TSchema::Define(stage->value, SdfPath(prim_path));
    if (!schema || !mark.IsClean())
    {
        std::string message = ConsumeErrors(mark);
        WriteError(error, message.empty() ? std::string("Could not define ") + schema_name + "." : message);
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    return OPENUSD_STATUS_OK;
}

openusd_status DefineGeomSchemaByKind(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t schema_kind,
    openusd_error_buffer* error)
{
    switch (schema_kind)
    {
        case OPENUSD_GEOM_SCHEMA_XFORM:
            return DefineSchema<UsdGeomXform>(stage, prim_path, "UsdGeomXform", error);
        case OPENUSD_GEOM_SCHEMA_MESH:
            return DefineSchema<UsdGeomMesh>(stage, prim_path, "UsdGeomMesh", error);
        case OPENUSD_GEOM_SCHEMA_CAMERA:
            return DefineSchema<UsdGeomCamera>(stage, prim_path, "UsdGeomCamera", error);
        case OPENUSD_GEOM_SCHEMA_SUBSET:
            return DefineSchema<UsdGeomSubset>(stage, prim_path, "UsdGeomSubset", error);
        case OPENUSD_GEOM_SCHEMA_BASIS_CURVES:
            return DefineSchema<UsdGeomBasisCurves>(stage, prim_path, "UsdGeomBasisCurves", error);
        case OPENUSD_GEOM_SCHEMA_NURBS_CURVES:
            return DefineSchema<UsdGeomNurbsCurves>(stage, prim_path, "UsdGeomNurbsCurves", error);
        case OPENUSD_GEOM_SCHEMA_HERMITE_CURVES:
            return DefineSchema<UsdGeomHermiteCurves>(stage, prim_path, "UsdGeomHermiteCurves", error);
        case OPENUSD_GEOM_SCHEMA_NURBS_PATCH:
            return DefineSchema<UsdGeomNurbsPatch>(stage, prim_path, "UsdGeomNurbsPatch", error);
        case OPENUSD_GEOM_SCHEMA_POINTS:
            return DefineSchema<UsdGeomPoints>(stage, prim_path, "UsdGeomPoints", error);
        case OPENUSD_GEOM_SCHEMA_POINT_INSTANCER:
            return DefineSchema<UsdGeomPointInstancer>(stage, prim_path, "UsdGeomPointInstancer", error);
        case OPENUSD_GEOM_SCHEMA_CAPSULE:
            return DefineSchema<UsdGeomCapsule>(stage, prim_path, "UsdGeomCapsule", error);
        case OPENUSD_GEOM_SCHEMA_CONE:
            return DefineSchema<UsdGeomCone>(stage, prim_path, "UsdGeomCone", error);
        case OPENUSD_GEOM_SCHEMA_CUBE:
            return DefineSchema<UsdGeomCube>(stage, prim_path, "UsdGeomCube", error);
        case OPENUSD_GEOM_SCHEMA_CYLINDER:
            return DefineSchema<UsdGeomCylinder>(stage, prim_path, "UsdGeomCylinder", error);
        case OPENUSD_GEOM_SCHEMA_SPHERE:
            return DefineSchema<UsdGeomSphere>(stage, prim_path, "UsdGeomSphere", error);
        case OPENUSD_GEOM_SCHEMA_PLANE:
            return DefineSchema<UsdGeomPlane>(stage, prim_path, "UsdGeomPlane", error);
        case OPENUSD_GEOM_SCHEMA_TET_MESH:
            return DefineSchema<UsdGeomTetMesh>(stage, prim_path, "UsdGeomTetMesh", error);
        default:
            WriteError(error, "The geometry schema kind cannot be defined as a concrete prim.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
}
}

openusd_status openusd_geom_is_schema(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t schema_kind,
    int32_t* matches,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(matches);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) || matches == nullptr)
        {
            WriteError(error, "A valid stage, absolute prim path, and match output are required.");
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

            bool result = false;
            switch (schema_kind)
            {
                case OPENUSD_GEOM_SCHEMA_IMAGEABLE:
                    result = static_cast<bool>(UsdGeomImageable(prim));
                    break;
                case OPENUSD_GEOM_SCHEMA_XFORMABLE:
                    result = static_cast<bool>(UsdGeomXformable(prim));
                    break;
                case OPENUSD_GEOM_SCHEMA_XFORM:
                    result = static_cast<bool>(UsdGeomXform(prim));
                    break;
                case OPENUSD_GEOM_SCHEMA_MESH:
                    result = static_cast<bool>(UsdGeomMesh(prim));
                    break;
                case OPENUSD_GEOM_SCHEMA_CAMERA:
                    result = static_cast<bool>(UsdGeomCamera(prim));
                    break;
                case OPENUSD_GEOM_SCHEMA_SUBSET:
                    result = IsSchema<UsdGeomSubset>(prim);
                    break;
                case OPENUSD_GEOM_SCHEMA_BASIS_CURVES:
                    result = IsSchema<UsdGeomBasisCurves>(prim);
                    break;
                case OPENUSD_GEOM_SCHEMA_NURBS_CURVES:
                    result = IsSchema<UsdGeomNurbsCurves>(prim);
                    break;
                case OPENUSD_GEOM_SCHEMA_HERMITE_CURVES:
                    result = IsSchema<UsdGeomHermiteCurves>(prim);
                    break;
                case OPENUSD_GEOM_SCHEMA_NURBS_PATCH:
                    result = IsSchema<UsdGeomNurbsPatch>(prim);
                    break;
                case OPENUSD_GEOM_SCHEMA_POINTS:
                    result = IsSchema<UsdGeomPoints>(prim);
                    break;
                case OPENUSD_GEOM_SCHEMA_POINT_INSTANCER:
                    result = IsSchema<UsdGeomPointInstancer>(prim);
                    break;
                case OPENUSD_GEOM_SCHEMA_CAPSULE:
                    result = IsSchema<UsdGeomCapsule>(prim);
                    break;
                case OPENUSD_GEOM_SCHEMA_CONE:
                    result = IsSchema<UsdGeomCone>(prim);
                    break;
                case OPENUSD_GEOM_SCHEMA_CUBE:
                    result = IsSchema<UsdGeomCube>(prim);
                    break;
                case OPENUSD_GEOM_SCHEMA_CYLINDER:
                    result = IsSchema<UsdGeomCylinder>(prim);
                    break;
                case OPENUSD_GEOM_SCHEMA_SPHERE:
                    result = IsSchema<UsdGeomSphere>(prim);
                    break;
                case OPENUSD_GEOM_SCHEMA_PLANE:
                    result = IsSchema<UsdGeomPlane>(prim);
                    break;
                case OPENUSD_GEOM_SCHEMA_TET_MESH:
                    result = IsSchema<UsdGeomTetMesh>(prim);
                    break;
                case OPENUSD_GEOM_SCHEMA_MODEL_API:
                    result = static_cast<bool>(UsdGeomModelAPI(prim));
                    break;
                case OPENUSD_GEOM_SCHEMA_PRIMVARS_API:
                    result = static_cast<bool>(UsdGeomPrimvarsAPI(prim));
                    break;
                default:
                    WriteError(error, "The geometry schema kind is invalid.");
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            *matches = result ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_define_xform(
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
            const UsdGeomXform schema = UsdGeomXform::Define(stage->value, SdfPath(prim_path));
            if (!schema || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not define the UsdGeomXform." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_define_mesh(
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
            const UsdGeomMesh schema = UsdGeomMesh::Define(stage->value, SdfPath(prim_path));
            if (!schema || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not define the UsdGeomMesh." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_define_camera(
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
            const UsdGeomCamera schema = UsdGeomCamera::Define(stage->value, SdfPath(prim_path));
            if (!schema || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not define the UsdGeomCamera." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_define_schema(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t schema_kind,
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
            return DefineGeomSchemaByKind(stage, prim_path, schema_kind, error);
        });

    });
}

openusd_status openusd_geom_set_int32_attr(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t value,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || attribute_name[0] == '\0')
        {
            WriteError(error, "A valid stage, prim path, and attribute name are required.");
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
            UsdAttribute attribute = prim.GetAttribute(TfToken(attribute_name));
            if (attribute && attribute.GetTypeName() != SdfValueTypeNames->Int)
            {
                WriteError(error, "The attribute is not an exact int value.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            if (!attribute)
            {
                attribute = prim.CreateAttribute(TfToken(attribute_name), SdfValueTypeNames->Int, true);
            }
            TfErrorMark mark;
            const bool set = attribute && attribute.Set(value, GetTimeCode(time_sampled, time_code));
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the int attribute." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_get_int32_attr(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    int32_t* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || attribute_name[0] == '\0' || value == nullptr)
        {
            WriteError(error, "A valid stage, prim path, attribute name, and output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            const UsdAttribute attribute = prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
            if (!attribute)
            {
                WriteError(error, "The requested int attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (attribute.GetTypeName() != SdfValueTypeNames->Int)
            {
                WriteError(error, "The attribute is not an exact int value.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            TfErrorMark mark;
            const bool read = attribute.Get(value, GetTimeCode(time_sampled, time_code));
            if (!read || !mark.IsClean())
            {
                const bool had_errors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "The attribute has no readable int value." : message);
                return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_imageable_set_visibility(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t visibility,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        TfToken token;
        if (!GetVisibilityToken(visibility, &token))
        {
            WriteError(error, "The geometry visibility value is invalid.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdGeomImageable schema;
            openusd_status status =
                GetGeomSchema(stage, prim_path, "UsdGeomImageable", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            TfErrorMark mark;
            const bool set = schema.CreateVisibilityAttr().Set(
                token, GetTimeCode(time_sampled, time_code));
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set visibility." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_imageable_get_visibility(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t time_sampled,
    double time_code,
    int32_t* visibility,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(visibility);
        if (visibility == nullptr)
        {
            WriteError(error, "A visibility output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdGeomImageable schema;
            openusd_status status =
                GetGeomSchema(stage, prim_path, "UsdGeomImageable", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            const TfToken token = schema.ComputeVisibility(GetTimeCode(time_sampled, time_code));
            if (!GetVisibilityValue(token, visibility))
            {
                WriteError(error, "OpenUSD returned an unsupported visibility token.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_imageable_set_purpose(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t purpose,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        TfToken token;
        if (!GetPurposeToken(purpose, &token))
        {
            WriteError(error, "The geometry purpose value is invalid.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdGeomImageable schema;
            openusd_status status =
                GetGeomSchema(stage, prim_path, "UsdGeomImageable", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            TfErrorMark mark;
            const bool set = schema.CreatePurposeAttr().Set(token);
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set purpose." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_imageable_get_purpose(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* purpose,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(purpose);
        if (purpose == nullptr)
        {
            WriteError(error, "A purpose output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdGeomImageable schema;
            openusd_status status =
                GetGeomSchema(stage, prim_path, "UsdGeomImageable", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            TfToken token;
            TfErrorMark mark;
            token = schema.ComputePurpose();
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not read purpose." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            if (!GetPurposeValue(token, purpose))
            {
                WriteError(error, "OpenUSD returned an unsupported purpose token.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_xformable_set_local_transform(
    const openusd_stage* stage,
    const char* prim_path,
    const openusd_matrix4d* value,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (value == nullptr || !IsAligned(value))
        {
            WriteError(error, "An aligned matrix value is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdGeomXformable schema;
            openusd_status status =
                GetGeomSchema(stage, prim_path, "UsdGeomXformable", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            GfMatrix4d matrix(0.0);
            for (int row = 0; row < 4; ++row)
            {
                for (int column = 0; column < 4; ++column)
                {
                    matrix[row][column] = value->values[(row * 4) + column];
                }
            }
            TfErrorMark mark;
            const UsdGeomXformOp operation = schema.MakeMatrixXform();
            const bool set = operation && operation.Set(
                matrix, GetTimeCode(time_sampled, time_code));
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the local transform." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_xformable_get_local_transform(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t time_sampled,
    double time_code,
    openusd_matrix4d* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (value == nullptr || !IsAligned(value))
        {
            WriteError(error, "An aligned matrix output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdGeomXformable schema;
            openusd_status status =
                GetGeomSchema(stage, prim_path, "UsdGeomXformable", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            GfMatrix4d matrix;
            bool resets = false;
            TfErrorMark mark;
            const bool read = schema.GetLocalTransformation(
                &matrix, &resets, GetTimeCode(time_sampled, time_code));
            if (!read || !mark.IsClean())
            {
                const bool had_errors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not read the local transform." : message);
                return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            for (int row = 0; row < 4; ++row)
            {
                for (int column = 0; column < 4; ++column)
                {
                    value->values[(row * 4) + column] = matrix[row][column];
                }
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_xformable_get_world_transform(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t time_sampled,
    double time_code,
    openusd_matrix4d* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            (time_sampled != 0 && time_sampled != 1) ||
            (time_sampled == 1 && !std::isfinite(time_code)) ||
            value == nullptr || !IsAligned(value))
        {
            WriteError(
                error,
                "A valid stage, absolute prim path, time, and aligned matrix output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                if (!mark.IsClean())
                {
                    std::string message = ConsumeErrors(mark);
                    WriteError(
                        error,
                        message.empty()
                            ? "Could not resolve the requested world-transform prim."
                            : message);
                    return OPENUSD_STATUS_NATIVE_ERROR;
                }
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (!prim.IsActive())
            {
                WriteError(
                    error,
                    std::string("World transforms are unavailable for inactive prims: ") +
                        prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (!UsdGeomXformable(prim))
            {
                WriteError(
                    error,
                    std::string("The prim is not compatible with UsdGeomXformable: ") +
                        prim_path);
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }

            UsdGeomXformCache cache(GetTimeCode(time_sampled, time_code));
            const GfMatrix4d matrix = cache.GetLocalToWorldTransform(prim);
            if (IsWorldTransformFailpoint("after-compute"))
            {
                TF_RUNTIME_ERROR("Injected world-transform diagnostic after compute.");
            }
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(
                    error,
                    message.empty() ? "Could not compute the requested world transform." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }

            const openusd_matrix4d result = FromMatrix4d(matrix);
            if (!IsFiniteMatrix(result))
            {
                WriteError(
                    error,
                    "The computed world transform contains a non-finite matrix element.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            *value = result;
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_xformable_set_reset_xform_stack(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t reset,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        return Guard(error, [&]()
        {
            UsdGeomXformable schema;
            openusd_status status =
                GetGeomSchema(stage, prim_path, "UsdGeomXformable", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            TfErrorMark mark;
            const bool set = schema.SetResetXformStack(reset != 0);
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set reset-xform-stack." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_xformable_get_reset_xform_stack(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* reset,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(reset);
        if (reset == nullptr)
        {
            WriteError(error, "A reset-xform-stack output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdGeomXformable schema;
            openusd_status status =
                GetGeomSchema(stage, prim_path, "UsdGeomXformable", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            *reset = schema.GetResetXformStack() ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_mesh_set_points(
    const openusd_stage* stage,
    const char* prim_path,
    const openusd_vec3f* values,
    size_t count,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        return Guard(error, [&]()
        {
            UsdGeomMesh schema;
            openusd_status status = GetGeomSchema(stage, prim_path, "UsdGeomMesh", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            return SetSchemaArray<openusd_vec3f, GfVec3f>(
                schema.CreatePointsAttr(),
                values,
                count,
                GetTimeCode(time_sampled, time_code),
                SdfValueTypeNames->Point3fArray,
                "mesh points",
                [](const openusd_vec3f& item) { return GfVec3f(item.x, item.y, item.z); },
                error);
        });

    });
}

openusd_status openusd_geom_mesh_get_points(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t time_sampled,
    double time_code,
    openusd_vec3f* values,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(required);
        return WithAbiWritableBuffer(values, capacity, [&]()
        {
            return Guard(error, [&]()
            {
                UsdGeomMesh schema;
                openusd_status status =
                    GetGeomSchema(stage, prim_path, "UsdGeomMesh", &schema, error);
                if (status != OPENUSD_STATUS_OK)
                {
                    return status;
                }
                return GetSchemaArray<openusd_vec3f, GfVec3f>(
                    schema.GetPointsAttr(),
                    GetTimeCode(time_sampled, time_code),
                    values,
                    capacity,
                    required,
                    SdfValueTypeNames->Point3fArray,
                    "mesh points",
                    [](const GfVec3f& item)
                    {
                        return openusd_vec3f{item[0], item[1], item[2]};
                    },
                    error);
            });
        });

    });
}

openusd_status openusd_geom_mesh_set_topology(
    const openusd_stage* stage,
    const char* prim_path,
    const int32_t* face_vertex_counts,
    size_t face_count,
    const int32_t* face_vertex_indices,
    size_t index_count,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!IsValidArrayBuffer(face_vertex_counts, face_count) ||
            !IsValidArrayBuffer(face_vertex_indices, index_count))
        {
            WriteError(error, "Aligned topology buffers and non-overflowing counts are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        size_t expected_indices = 0;
        for (size_t index = 0; index < face_count; ++index)
        {
            if (face_vertex_counts[index] < 0 ||
                expected_indices > std::numeric_limits<size_t>::max() -
                    static_cast<size_t>(face_vertex_counts[index]))
            {
                WriteError(error, "Face vertex counts must be non-negative and non-overflowing.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            expected_indices += static_cast<size_t>(face_vertex_counts[index]);
        }
        if (expected_indices != index_count)
        {
            WriteError(error, "The sum of face vertex counts must equal the index count.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        for (size_t index = 0; index < index_count; ++index)
        {
            if (face_vertex_indices[index] < 0)
            {
                WriteError(error, "Face vertex indices must be non-negative.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
        }

        return Guard(error, [&]()
        {
            UsdGeomMesh schema;
            openusd_status status = GetGeomSchema(stage, prim_path, "UsdGeomMesh", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            VtArray<int> counts(face_count);
            VtArray<int> indices(index_count);
            if (face_count != 0)
            {
                std::copy(face_vertex_counts, face_vertex_counts + face_count, counts.begin());
            }
            if (index_count != 0)
            {
                std::copy(face_vertex_indices, face_vertex_indices + index_count, indices.begin());
            }
            TfErrorMark mark;
            const bool counts_set = schema.CreateFaceVertexCountsAttr().Set(counts);
            const bool indices_set = schema.CreateFaceVertexIndicesAttr().Set(indices);
            if (!counts_set || !indices_set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set mesh topology." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_mesh_get_face_vertex_counts(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* values,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(required);
        return WithAbiWritableBuffer(values, capacity, [&]()
        {
            return Guard(error, [&]()
            {
                UsdGeomMesh schema;
                openusd_status status =
                    GetGeomSchema(stage, prim_path, "UsdGeomMesh", &schema, error);
                if (status != OPENUSD_STATUS_OK)
                {
                    return status;
                }
                return GetSchemaArray<int32_t, int>(
                    schema.GetFaceVertexCountsAttr(),
                    UsdTimeCode::Default(),
                    values,
                    capacity,
                    required,
                    SdfValueTypeNames->IntArray,
                    "mesh face vertex counts",
                    [](int item) { return static_cast<int32_t>(item); },
                    error);
            });
        });

    });
}

openusd_status openusd_geom_mesh_get_face_vertex_indices(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* values,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(required);
        return WithAbiWritableBuffer(values, capacity, [&]()
        {
            return Guard(error, [&]()
            {
                UsdGeomMesh schema;
                openusd_status status =
                    GetGeomSchema(stage, prim_path, "UsdGeomMesh", &schema, error);
                if (status != OPENUSD_STATUS_OK)
                {
                    return status;
                }
                return GetSchemaArray<int32_t, int>(
                    schema.GetFaceVertexIndicesAttr(),
                    UsdTimeCode::Default(),
                    values,
                    capacity,
                    required,
                    SdfValueTypeNames->IntArray,
                    "mesh face vertex indices",
                    [](int item) { return static_cast<int32_t>(item); },
                    error);
            });
        });

    });
}

openusd_status openusd_geom_mesh_set_normals(
    const openusd_stage* stage,
    const char* prim_path,
    const openusd_vec3f* values,
    size_t count,
    int32_t interpolation,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        TfToken token;
        if (!GetInterpolationToken(interpolation, &token))
        {
            WriteError(error, "The normals interpolation value is invalid.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdGeomMesh schema;
            openusd_status status = GetGeomSchema(stage, prim_path, "UsdGeomMesh", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            const UsdTimeCode time = GetTimeCode(time_sampled, time_code);
            status = ValidateMeshNormalsCardinality(
                schema, token, count, time, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            status = SetSchemaArray<openusd_vec3f, GfVec3f>(
                schema.CreateNormalsAttr(),
                values,
                count,
                time,
                SdfValueTypeNames->Normal3fArray,
                "mesh normals",
                [](const openusd_vec3f& item) { return GfVec3f(item.x, item.y, item.z); },
                error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            TfErrorMark mark;
            const bool set = schema.SetNormalsInterpolation(token);
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set normals interpolation." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_mesh_get_normals(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t time_sampled,
    double time_code,
    openusd_vec3f* values,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(required);
        return WithAbiWritableBuffer(values, capacity, [&]()
        {
            return Guard(error, [&]()
            {
                UsdGeomMesh schema;
                openusd_status status =
                    GetGeomSchema(stage, prim_path, "UsdGeomMesh", &schema, error);
                if (status != OPENUSD_STATUS_OK)
                {
                    return status;
                }
                return GetSchemaArray<openusd_vec3f, GfVec3f>(
                    schema.GetNormalsAttr(),
                    GetTimeCode(time_sampled, time_code),
                    values,
                    capacity,
                    required,
                    SdfValueTypeNames->Normal3fArray,
                    "mesh normals",
                    [](const GfVec3f& item)
                    {
                        return openusd_vec3f{item[0], item[1], item[2]};
                    },
                    error);
            });
        });

    });
}

openusd_status openusd_geom_mesh_set_normals_interpolation(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t interpolation,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        TfToken token;
        if (!GetInterpolationToken(interpolation, &token))
        {
            WriteError(error, "The normals interpolation value is invalid.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdGeomMesh schema;
            openusd_status status = GetGeomSchema(stage, prim_path, "UsdGeomMesh", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            TfErrorMark mark;
            const bool set = schema.SetNormalsInterpolation(token);
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set normals interpolation." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_mesh_get_normals_interpolation(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* interpolation,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(interpolation);
        if (interpolation == nullptr)
        {
            WriteError(error, "A normals interpolation output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdGeomMesh schema;
            openusd_status status = GetGeomSchema(stage, prim_path, "UsdGeomMesh", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            if (!GetInterpolationValue(schema.GetNormalsInterpolation(), interpolation))
            {
                WriteError(error, "OpenUSD returned an unsupported normals interpolation token.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_mesh_set_subdivision_scheme(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t scheme,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        TfToken token;
        if (!GetSubdivisionToken(scheme, &token))
        {
            WriteError(error, "The subdivision scheme value is invalid.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdGeomMesh schema;
            openusd_status status = GetGeomSchema(stage, prim_path, "UsdGeomMesh", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            TfErrorMark mark;
            const bool set = schema.CreateSubdivisionSchemeAttr().Set(token);
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set subdivision scheme." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_mesh_get_subdivision_scheme(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* scheme,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(scheme);
        if (scheme == nullptr)
        {
            WriteError(error, "A subdivision scheme output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdGeomMesh schema;
            openusd_status status = GetGeomSchema(stage, prim_path, "UsdGeomMesh", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            TfToken token;
            TfErrorMark mark;
            const bool read = schema.GetSubdivisionSchemeAttr().Get(&token);
            if (!read || !mark.IsClean())
            {
                const bool had_errors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not read subdivision scheme." : message);
                return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            if (!GetSubdivisionValue(token, scheme))
            {
                WriteError(error, "OpenUSD returned an unsupported subdivision scheme token.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_mesh_set_orientation(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t orientation,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        TfToken token;
        if (!GetOrientationToken(orientation, &token))
        {
            WriteError(error, "The mesh orientation value is invalid.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdGeomMesh schema;
            openusd_status status = GetGeomSchema(stage, prim_path, "UsdGeomMesh", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            TfErrorMark mark;
            const bool set = schema.CreateOrientationAttr().Set(token);
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set mesh orientation." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_mesh_get_orientation(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* orientation,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(orientation);
        if (orientation == nullptr)
        {
            WriteError(error, "A mesh orientation output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdGeomMesh schema;
            openusd_status status = GetGeomSchema(stage, prim_path, "UsdGeomMesh", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            TfToken token;
            TfErrorMark mark;
            const bool read = schema.GetOrientationAttr().Get(&token);
            if (!read || !mark.IsClean())
            {
                const bool had_errors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not read mesh orientation." : message);
                return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            if (!GetOrientationValue(token, orientation))
            {
                WriteError(error, "OpenUSD returned an unsupported orientation token.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_mesh_set_double_sided(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t double_sided,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        return Guard(error, [&]()
        {
            UsdGeomMesh schema;
            openusd_status status = GetGeomSchema(stage, prim_path, "UsdGeomMesh", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            TfErrorMark mark;
            const bool set = schema.CreateDoubleSidedAttr().Set(double_sided != 0);
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set double-sided." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_mesh_get_double_sided(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* double_sided,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(double_sided);
        if (double_sided == nullptr)
        {
            WriteError(error, "A double-sided output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdGeomMesh schema;
            openusd_status status = GetGeomSchema(stage, prim_path, "UsdGeomMesh", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            bool value = false;
            TfErrorMark mark;
            const bool read = schema.GetDoubleSidedAttr().Get(&value);
            if (!read || !mark.IsClean())
            {
                const bool had_errors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not read double-sided." : message);
                return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            *double_sided = value ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_mesh_set_extent(
    const openusd_stage* stage,
    const char* prim_path,
    const openusd_extent3f* extent,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (extent == nullptr || !IsAligned(extent) ||
            extent->minimum.x > extent->maximum.x ||
            extent->minimum.y > extent->maximum.y ||
            extent->minimum.z > extent->maximum.z)
        {
            WriteError(error, "An aligned extent with minimum not exceeding maximum is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdGeomMesh schema;
            openusd_status status = GetGeomSchema(stage, prim_path, "UsdGeomMesh", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            VtArray<GfVec3f> value(2);
            value[0] = GfVec3f(extent->minimum.x, extent->minimum.y, extent->minimum.z);
            value[1] = GfVec3f(extent->maximum.x, extent->maximum.y, extent->maximum.z);
            TfErrorMark mark;
            const bool set = schema.CreateExtentAttr().Set(
                value, GetTimeCode(time_sampled, time_code));
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set mesh extent." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_mesh_get_extent(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t time_sampled,
    double time_code,
    openusd_extent3f* extent,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(extent);
        if (extent == nullptr || !IsAligned(extent))
        {
            WriteError(error, "An aligned extent output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdGeomMesh schema;
            openusd_status status = GetGeomSchema(stage, prim_path, "UsdGeomMesh", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            VtArray<GfVec3f> value;
            TfErrorMark mark;
            const bool read = schema.GetExtentAttr().Get(
                &value, GetTimeCode(time_sampled, time_code));
            if (!read || !mark.IsClean())
            {
                const bool hadErrors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "The mesh extent has no readable value." : message);
                return hadErrors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            if (value.size() != 2)
            {
                WriteError(error, "The mesh extent does not contain exactly two values.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            extent->minimum = {value[0][0], value[0][1], value[0][2]};
            extent->maximum = {value[1][0], value[1][1], value[1][2]};
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_camera_set_projection(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t projection,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        TfToken token;
        if (!GetProjectionToken(projection, &token))
        {
            WriteError(error, "The camera projection value is invalid.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdGeomCamera schema;
            openusd_status status = GetGeomSchema(stage, prim_path, "UsdGeomCamera", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            TfErrorMark mark;
            const bool set = schema.CreateProjectionAttr().Set(token);
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set camera projection." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_camera_get_projection(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* projection,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(projection);
        if (projection == nullptr)
        {
            WriteError(error, "A camera projection output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdGeomCamera schema;
            openusd_status status = GetGeomSchema(stage, prim_path, "UsdGeomCamera", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            TfToken token;
            TfErrorMark mark;
            const bool read = schema.GetProjectionAttr().Get(&token);
            if (!read || !mark.IsClean())
            {
                const bool had_errors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not read camera projection." : message);
                return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            if (!GetProjectionValue(token, projection))
            {
                WriteError(error, "OpenUSD returned an unsupported camera projection token.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_camera_set_float_property(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t property,
    float value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        const bool aperture_property =
            property == OPENUSD_GEOM_CAMERA_HORIZONTAL_APERTURE ||
            property == OPENUSD_GEOM_CAMERA_VERTICAL_APERTURE;
        if (!std::isfinite(value) || value < 0.0F ||
            (aperture_property && value == 0.0F))
        {
            WriteError(
                error,
                "Camera focal length must be finite and non-negative; apertures "
                "must be finite and positive.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdGeomCamera schema;
            openusd_status status = GetGeomSchema(stage, prim_path, "UsdGeomCamera", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            UsdAttribute attribute;
            switch (property)
            {
                case OPENUSD_GEOM_CAMERA_FOCAL_LENGTH:
                    attribute = schema.CreateFocalLengthAttr();
                    break;
                case OPENUSD_GEOM_CAMERA_HORIZONTAL_APERTURE:
                    attribute = schema.CreateHorizontalApertureAttr();
                    break;
                case OPENUSD_GEOM_CAMERA_VERTICAL_APERTURE:
                    attribute = schema.CreateVerticalApertureAttr();
                    break;
                default:
                    WriteError(error, "The camera float property is invalid.");
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            TfErrorMark mark;
            if (property == OPENUSD_GEOM_CAMERA_FOCAL_LENGTH && value == 0.0F)
            {
                TfToken projection;
                const bool read = schema.GetProjectionAttr().Get(&projection);
                if (!read || !mark.IsClean())
                {
                    std::string message = ConsumeErrors(mark);
                    WriteError(
                        error,
                        message.empty()
                            ? "Could not read camera projection for zero focal length."
                            : message);
                    return OPENUSD_STATUS_NATIVE_ERROR;
                }
                if (projection != UsdGeomTokens->perspective &&
                    projection != UsdGeomTokens->orthographic)
                {
                    WriteError(
                        error,
                        "OpenUSD returned an unsupported camera projection token.");
                    return OPENUSD_STATUS_NATIVE_ERROR;
                }
                if (projection != UsdGeomTokens->orthographic)
                {
                    WriteError(
                        error,
                        "Zero focal length is valid only for an orthographic camera.");
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
                }
            }
            const bool set = attribute.Set(value);
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the camera property." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_camera_get_float_property(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t property,
    float* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (value == nullptr)
        {
            WriteError(error, "A camera float output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdGeomCamera schema;
            openusd_status status = GetGeomSchema(stage, prim_path, "UsdGeomCamera", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            UsdAttribute attribute;
            switch (property)
            {
                case OPENUSD_GEOM_CAMERA_FOCAL_LENGTH:
                    attribute = schema.GetFocalLengthAttr();
                    break;
                case OPENUSD_GEOM_CAMERA_HORIZONTAL_APERTURE:
                    attribute = schema.GetHorizontalApertureAttr();
                    break;
                case OPENUSD_GEOM_CAMERA_VERTICAL_APERTURE:
                    attribute = schema.GetVerticalApertureAttr();
                    break;
                default:
                    WriteError(error, "The camera float property is invalid.");
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            TfErrorMark mark;
            const bool read = attribute.Get(value);
            if (!read || !mark.IsClean())
            {
                const bool had_errors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not read the camera property." : message);
                return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_camera_set_clipping_range(
    const openusd_stage* stage,
    const char* prim_path,
    const openusd_vec2f* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (value == nullptr || !IsAligned(value) ||
            !std::isfinite(value->x) || !std::isfinite(value->y) ||
            value->x <= 0.0F || value->y <= value->x)
        {
            WriteError(error, "The clipping range must contain finite positive near and larger far values.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdGeomCamera schema;
            openusd_status status = GetGeomSchema(stage, prim_path, "UsdGeomCamera", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            TfErrorMark mark;
            const bool set = schema.CreateClippingRangeAttr().Set(GfVec2f(value->x, value->y));
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set camera clipping range." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_camera_get_clipping_range(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_vec2f* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (value == nullptr || !IsAligned(value))
        {
            WriteError(error, "An aligned clipping-range output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdGeomCamera schema;
            openusd_status status = GetGeomSchema(stage, prim_path, "UsdGeomCamera", &schema, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            GfVec2f range;
            TfErrorMark mark;
            const bool read = schema.GetClippingRangeAttr().Get(&range);
            if (!read || !mark.IsClean())
            {
                const bool had_errors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not read camera clipping range." : message);
                return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            value->x = range[0];
            value->y = range[1];
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_geom_camera_get_state(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t time_sampled,
    double time_code,
    openusd_geom_camera_state* state,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        uint32_t struct_size = 0;
        uint32_t requested_version = 0;
        if (state != nullptr)
        {
            std::memcpy(&struct_size, state, sizeof(struct_size));
            if (struct_size >=
                offsetof(openusd_geom_camera_state, version) + sizeof(uint32_t))
            {
                std::memcpy(
                    &requested_version,
                    reinterpret_cast<const unsigned char*>(state) +
                        offsetof(openusd_geom_camera_state, version),
                    sizeof(requested_version));
            }
        }

        // ABI_OUTPUT_INITIALIZATION
        ResetCameraStateOutput(state);
        CameraStateFailureReset failure_reset(state);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            state == nullptr || !IsAligned(state) ||
            struct_size < sizeof(openusd_geom_camera_state) ||
            requested_version != OPENUSD_GEOM_CAMERA_STATE_VERSION ||
            (time_sampled != 0 && time_sampled != 1) ||
            (time_sampled == 1 && !std::isfinite(time_code)))
        {
            WriteError(
                error,
                "A valid stage, absolute camera path, time, and aligned camera-state "
                "output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        TfErrorMark mark;
        const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
        if (!prim || !prim.IsActive())
        {
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(
                    error,
                    message.empty()
                        ? "Could not resolve the requested camera prim."
                        : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            WriteError(error, std::string("Camera prim was not found or active: ") + prim_path);
            return OPENUSD_STATUS_NOT_FOUND;
        }

        const UsdGeomCamera schema(prim);
        if (!schema)
        {
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(
                    error,
                    message.empty() ? "Could not inspect the requested camera prim." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            WriteError(
                error,
                std::string("The prim is not compatible with UsdGeomCamera: ") + prim_path);
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        const UsdTimeCode time = GetTimeCode(time_sampled, time_code);
        TfToken projection_token;
        const bool projection_read =
            schema.GetProjectionAttr().Get(&projection_token, time);
        if (!projection_read || !mark.IsClean())
        {
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(
                    error,
                    message.empty()
                        ? "Could not evaluate the camera projection."
                        : message);
            }
            else
            {
                WriteError(error, "The camera projection has no readable value.");
            }
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        if (projection_token != UsdGeomTokens->perspective &&
            projection_token != UsdGeomTokens->orthographic)
        {
            WriteError(error, "OpenUSD returned an unsupported camera projection token.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }

        const GfCamera camera = schema.GetCamera(time);
        const GfFrustum frustum = camera.GetFrustum();
        if (IsCameraStateFailpoint("after-compute"))
        {
            TF_RUNTIME_ERROR("Injected camera-state failure after computation.");
        }
        if (!mark.IsClean())
        {
            std::string message = ConsumeErrors(mark);
            WriteError(
                error,
                message.empty() ? "Could not evaluate the camera state." : message);
            return OPENUSD_STATUS_NATIVE_ERROR;
        }

        int32_t projection = 0;
        if (camera.GetProjection() == GfCamera::Perspective &&
            frustum.GetProjectionType() == GfFrustum::Perspective)
        {
            projection = OPENUSD_GEOM_CAMERA_PERSPECTIVE;
        }
        else if (camera.GetProjection() == GfCamera::Orthographic &&
                 frustum.GetProjectionType() == GfFrustum::Orthographic)
        {
            projection = OPENUSD_GEOM_CAMERA_ORTHOGRAPHIC;
        }
        else
        {
            WriteError(error, "OpenUSD returned an inconsistent camera projection.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }

        const GfRange2d& window = frustum.GetWindow();
        const GfRange1d& near_far = frustum.GetNearFar();
        const double window_left = window.GetMin()[0];
        const double window_right = window.GetMax()[0];
        const double window_bottom = window.GetMin()[1];
        const double window_top = window.GetMax()[1];
        const double clipping_near = near_far.GetMin();
        const double clipping_far = near_far.GetMax();
        const double focal_length = camera.GetFocalLength();
        const double horizontal_aperture = camera.GetHorizontalAperture();
        const double vertical_aperture = camera.GetVerticalAperture();
        const double horizontal_aperture_offset =
            camera.GetHorizontalApertureOffset();
        const double vertical_aperture_offset =
            camera.GetVerticalApertureOffset();
        const double focus_distance = camera.GetFocusDistance();
        const double f_stop = camera.GetFStop();
        const double window_width = window_right - window_left;
        const double window_height = window_top - window_bottom;
        const double clipping_depth = clipping_far - clipping_near;
        const double window_center_x = (window_left / 2.0) + (window_right / 2.0);
        const double window_center_y = (window_bottom / 2.0) + (window_top / 2.0);
        const bool finite =
            IsFiniteMatrix(FromMatrix4d(camera.GetTransform())) &&
            std::isfinite(window_left) &&
            std::isfinite(window_right) &&
            std::isfinite(window_bottom) &&
            std::isfinite(window_top) &&
            std::isfinite(clipping_near) &&
            std::isfinite(clipping_far) &&
            std::isfinite(focal_length) &&
            std::isfinite(horizontal_aperture) &&
            std::isfinite(vertical_aperture) &&
            std::isfinite(horizontal_aperture_offset) &&
            std::isfinite(vertical_aperture_offset) &&
            std::isfinite(focus_distance) &&
            std::isfinite(f_stop) &&
            std::isfinite(window_width) &&
            std::isfinite(window_height) &&
            std::isfinite(clipping_depth) &&
            std::isfinite(window_center_x) &&
            std::isfinite(window_center_y);
        const bool valid_frustum =
            window_left < window_right &&
            window_bottom < window_top &&
            clipping_near < clipping_far &&
            (projection != OPENUSD_GEOM_CAMERA_PERSPECTIVE ||
             clipping_near > 0.0);
        const bool valid_optics =
            focal_length >= 0.0 &&
            (projection != OPENUSD_GEOM_CAMERA_PERSPECTIVE ||
             focal_length > 0.0) &&
            horizontal_aperture > 0.0 &&
            vertical_aperture > 0.0 &&
            focus_distance >= 0.0 &&
            f_stop >= 0.0;
        if (!finite || !valid_frustum || !valid_optics)
        {
            WriteError(error, "OpenUSD returned a non-finite or invalid camera frustum.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }

        state->projection = projection;
        state->window_left = window_left;
        state->window_right = window_right;
        state->window_bottom = window_bottom;
        state->window_top = window_top;
        state->clipping_near = clipping_near;
        state->clipping_far = clipping_far;
        state->focal_length = focal_length;
        state->horizontal_aperture = horizontal_aperture;
        state->vertical_aperture = vertical_aperture;
        state->horizontal_aperture_offset = horizontal_aperture_offset;
        state->vertical_aperture_offset = vertical_aperture_offset;
        state->focus_distance = focus_distance;
        state->f_stop = f_stop;
        state->is_valid = 1;
        failure_reset.Commit();
        return OPENUSD_STATUS_OK;
    });
}
