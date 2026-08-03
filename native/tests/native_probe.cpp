// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_dotnet.h"
#include "openusd_dotnet_test_hooks.h"

#include <pxr/base/gf/frustum.h>
#include <pxr/base/gf/matrix4d.h>
#include <pxr/base/gf/range1d.h>
#include <pxr/base/gf/range2d.h>
#include <pxr/base/gf/range3d.h>
#include <pxr/base/gf/vec3d.h>
#include <pxr/base/gf/vec3f.h>
#include <pxr/base/tf/errorMark.h>
#include <pxr/usd/pcp/composeSite.h>
#include <pxr/usd/pcp/errors.h>
#include <pxr/usd/sdf/layer.h>
#include <pxr/usd/sdf/payload.h>
#include <pxr/usd/sdf/path.h>
#include <pxr/usd/usd/payloads.h>
#include <pxr/usd/usd/references.h>
#include <pxr/usd/usd/stage.h>
#include <pxr/usd/usd/variantSets.h>
#include <pxr/usd/usdGeom/bboxCache.h>
#include <pxr/usd/usdGeom/camera.h>
#include <pxr/usd/usdGeom/tokens.h>
#include <pxr/usd/usdGeom/xform.h>
#include <pxr/usd/usdGeom/xformCache.h>
#include <pxr/usd/usdGeom/xformable.h>

#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <limits>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
class ThreadBarrier final
{
public:
    explicit ThreadBarrier(size_t participants)
        : _participants(participants)
        , _remaining(participants)
    {
    }

    void ArriveAndWait()
    {
        std::unique_lock<std::mutex> lock(_mutex);
        const size_t generation = _generation;
        if (--_remaining == 0)
        {
            ++_generation;
            _remaining = _participants;
            _condition.notify_all();
            return;
        }
        _condition.wait(lock, [&]() { return _generation != generation; });
    }

private:
    const size_t _participants;
    size_t _remaining;
    size_t _generation = 0;
    std::mutex _mutex;
    std::condition_variable _condition;
};

void SetStageAccessBeginFailpoint(const char* value)
{
#if defined(_WIN32)
    _putenv_s("OPENUSD_DOTNET_STAGE_ACCESS_BEGIN_FAILPOINT", value == nullptr ? "" : value);
#else
    if (value == nullptr)
    {
        unsetenv("OPENUSD_DOTNET_STAGE_ACCESS_BEGIN_FAILPOINT");
    }
    else
    {
        setenv("OPENUSD_DOTNET_STAGE_ACCESS_BEGIN_FAILPOINT", value, 1);
    }
#endif
}

void SetCompositionEnumerationFailpoint(const char* value)
{
#if defined(_WIN32)
    _putenv_s(
        "OPENUSD_DOTNET_COMPOSITION_ENUMERATION_FAILPOINT",
        value == nullptr ? "" : value);
#else
    if (value == nullptr)
    {
        unsetenv("OPENUSD_DOTNET_COMPOSITION_ENUMERATION_FAILPOINT");
    }
    else
    {
        setenv("OPENUSD_DOTNET_COMPOSITION_ENUMERATION_FAILPOINT", value, 1);
    }
#endif
}

void SetWorldTransformFailpoint(const char* value)
{
#if defined(_WIN32)
    _putenv_s(
        "OPENUSD_DOTNET_WORLD_TRANSFORM_FAILPOINT",
        value == nullptr ? "" : value);
#else
    if (value == nullptr)
    {
        unsetenv("OPENUSD_DOTNET_WORLD_TRANSFORM_FAILPOINT");
    }
    else
    {
        setenv("OPENUSD_DOTNET_WORLD_TRANSFORM_FAILPOINT", value, 1);
    }
#endif
}

void SetCameraStateFailpoint(const char* value)
{
#if defined(_WIN32)
    _putenv_s(
        "OPENUSD_DOTNET_CAMERA_STATE_FAILPOINT",
        value == nullptr ? "" : value);
#else
    if (value == nullptr)
    {
        unsetenv("OPENUSD_DOTNET_CAMERA_STATE_FAILPOINT");
    }
    else
    {
        setenv("OPENUSD_DOTNET_CAMERA_STATE_FAILPOINT", value, 1);
    }
#endif
}

using PrimStringListGetter = openusd_status (*)(
    const openusd_stage*,
    const char*,
    openusd_string_list**,
    openusd_string_list_view*,
    openusd_error_buffer*);

using StageStringGetter = openusd_status (*)(
    const openusd_stage*,
    char*,
    size_t,
    size_t*,
    openusd_error_buffer*);

using StageStringListGetter = openusd_status (*)(
    const openusd_stage*,
    openusd_string_list**,
    openusd_string_list_view*,
    openusd_error_buffer*);

using PrimStringGetter = openusd_status (*)(
    const openusd_stage*,
    const char*,
    char*,
    size_t,
    size_t*,
    openusd_error_buffer*);

template <typename TValue>
using ArrayGetter = openusd_status (*)(
    const openusd_stage*,
    const char*,
    const char*,
    int32_t,
    double,
    TValue*,
    size_t,
    size_t*,
    openusd_error_buffer*);

template <typename TValue>
using GeomTimedArrayGetter = openusd_status (*)(
    const openusd_stage*,
    const char*,
    int32_t,
    double,
    TValue*,
    size_t,
    size_t*,
    openusd_error_buffer*);

template <typename TValue>
using GeomArrayGetter = openusd_status (*)(
    const openusd_stage*,
    const char*,
    TValue*,
    size_t,
    size_t*,
    openusd_error_buffer*);

template <typename TValue>
bool IsZeroed(const TValue* values, size_t count)
{
    const auto* bytes = reinterpret_cast<const unsigned char*>(values);
    return std::all_of(
        bytes,
        bytes + count * sizeof(TValue),
        [](unsigned char value) { return value == 0; });
}

template <typename TValue, typename TGetter>
bool VerifyWritableArrayGetter(
    const openusd_stage* stage,
    const char* authoredPath,
    size_t expectedCount,
    TGetter&& getter,
    openusd_error_buffer* error)
{
    const auto verifyFailure =
        [&](const openusd_stage* inputStage,
            const char* primPath,
            openusd_status expectedStatus)
        {
            std::array<TValue, 3> values;
            std::memset(values.data(), 0xa5, sizeof(values));
            size_t required = 31337;
            const openusd_status status =
                getter(inputStage, primPath, values.data(), values.size(), &required, error);
            return status == expectedStatus && required == 0 &&
                IsZeroed(values.data(), values.size());
        };

    if (!verifyFailure(nullptr, authoredPath, OPENUSD_STATUS_INVALID_ARGUMENT) ||
        !verifyFailure(stage, "/__BufferSentinels/Missing", OPENUSD_STATUS_NOT_FOUND))
    {
        return false;
    }

    size_t required = 31337;
    if (getter(stage, authoredPath, nullptr, 0, &required, error) != OPENUSD_STATUS_OK ||
        required != expectedCount)
    {
        return false;
    }

    std::array<TValue, 1> tooSmall;
    std::memset(tooSmall.data(), 0xa5, sizeof(tooSmall));
    required = 31337;
    return getter(
               stage,
               authoredPath,
               tooSmall.data(),
               tooSmall.size(),
               &required,
               error) == OPENUSD_STATUS_BUFFER_TOO_SMALL &&
        required == 0 && IsZeroed(tooSmall.data(), tooSmall.size());
}

std::string ReadVersion()
{
    size_t required = 0;
    if (openusd_get_version(nullptr, 0, &required) != OPENUSD_STATUS_BUFFER_TOO_SMALL)
    {
        return {};
    }

    std::vector<char> buffer(required);
    if (openusd_get_version(buffer.data(), buffer.size(), &required) != OPENUSD_STATUS_OK)
    {
        return {};
    }
    return buffer.data();
}

bool ReadPrimStringList(
    const openusd_stage* stage,
    const char* primPath,
    PrimStringListGetter getter,
    openusd_error_buffer* error,
    std::vector<std::string>* values)
{
    openusd_string_list* list = nullptr;
    openusd_string_list_view view{
        static_cast<uint32_t>(sizeof(openusd_string_list_view)), nullptr, 0, nullptr, 0, 0};
    const openusd_status status = getter(stage, primPath, &list, &view, error);
    if (status != OPENUSD_STATUS_OK)
    {
        return false;
    }

    values->clear();
    values->reserve(view.count);
    for (size_t i = 0; i < view.count; ++i)
    {
        values->emplace_back(view.data + view.offsets[i]);
    }
    openusd_string_list_release(list);
    return true;
}

struct PayloadArcResult
{
    std::string assetPath;
    std::string targetPrimPath;
    std::string sourceLayerIdentifier;
};

bool operator==(const PayloadArcResult& left, const PayloadArcResult& right)
{
    return left.assetPath == right.assetPath &&
        left.targetPrimPath == right.targetPrimPath &&
        left.sourceLayerIdentifier == right.sourceLayerIdentifier;
}

bool ReadPayloadArcList(
    const openusd_stage* stage,
    const char* primPath,
    openusd_error_buffer* error,
    std::vector<PayloadArcResult>* values,
    openusd_status* outputStatus = nullptr)
{
    openusd_payload_arc_list* list = nullptr;
    openusd_payload_arc_list_view view{
        static_cast<uint32_t>(sizeof(openusd_payload_arc_list_view)),
        OPENUSD_PAYLOAD_ARC_LIST_VIEW_VERSION,
        nullptr,
        0,
        nullptr,
        0,
        0};
    const openusd_status status = openusd_stage_get_composed_payload_arcs(
        stage,
        primPath,
        &list,
        &view,
        error);
    if (outputStatus != nullptr)
    {
        *outputStatus = status;
    }
    if (status != OPENUSD_STATUS_OK)
    {
        openusd_payload_arc_list_release(list);
        return false;
    }

    const size_t entryCount = view.count * 3;
    if (view.version != OPENUSD_PAYLOAD_ARC_LIST_VIEW_VERSION ||
        view.offsets_size != entryCount * sizeof(size_t) ||
        (view.count == 0 &&
         (view.data != nullptr || view.data_size != 0 ||
          view.offsets != nullptr || view.offsets_size != 0)) ||
        (view.count != 0 &&
         (view.data == nullptr || view.offsets == nullptr)))
    {
        openusd_payload_arc_list_release(list);
        return false;
    }

    std::vector<std::string> fields;
    fields.reserve(entryCount);
    size_t expectedOffset = 0;
    for (size_t index = 0; index < entryCount; ++index)
    {
        if (view.offsets[index] != expectedOffset ||
            view.offsets[index] >= view.data_size)
        {
            openusd_payload_arc_list_release(list);
            return false;
        }
        const char* begin = view.data + view.offsets[index];
        const auto* end = static_cast<const char*>(
            std::memchr(begin, '\0', view.data_size - view.offsets[index]));
        if (end == nullptr)
        {
            openusd_payload_arc_list_release(list);
            return false;
        }
        fields.emplace_back(begin, end);
        expectedOffset = static_cast<size_t>(end - view.data) + 1;
    }
    if (expectedOffset != view.data_size)
    {
        openusd_payload_arc_list_release(list);
        return false;
    }

    values->clear();
    values->reserve(view.count);
    for (size_t index = 0; index < view.count; ++index)
    {
        const size_t field = index * 3;
        values->push_back(
            PayloadArcResult{fields[field], fields[field + 1], fields[field + 2]});
    }
    openusd_payload_arc_list_release(list);
    return true;
}

std::vector<PayloadArcResult> ReadReferencePayloadArcs(const UsdPrim& prim)
{
    std::vector<PayloadArcResult> values;
    const PcpPrimIndex primIndex = prim.ComputeExpandedPrimIndex();
    for (const PcpNodeRef& node : primIndex.GetNodeRange())
    {
        if (node.IsDueToAncestor())
        {
            continue;
        }

        SdfPayloadVector payloads;
        PcpArcInfoVector arcInfo;
        PcpErrorVector errors;
        PcpComposeSitePayloads(node, &payloads, &arcInfo, nullptr, &errors);
        if (!errors.empty() || payloads.size() != arcInfo.size())
        {
            return {};
        }
        for (size_t index = 0; index < payloads.size(); ++index)
        {
            if (!arcInfo[index].sourceLayer)
            {
                return {};
            }
            values.push_back(
                PayloadArcResult{
                    arcInfo[index].authoredAssetPath,
                    payloads[index].GetPrimPath().GetString(),
                    arcInfo[index].sourceLayer->GetIdentifier()});
        }
    }
    return values;
}

bool VerifyFailedOutputInitialization(
    const openusd_stage* validStage,
    openusd_error_buffer* error)
{
    const auto makeSentinelView = []()
    {
        return openusd_string_list_view{
            static_cast<uint32_t>(sizeof(openusd_string_list_view)),
            reinterpret_cast<const char*>(uintptr_t{1}),
            17,
            reinterpret_cast<const size_t*>(uintptr_t{1}),
            19,
            23};
    };
    const auto isResetView = [](const openusd_string_list_view& value)
    {
        return value.struct_size == sizeof(openusd_string_list_view) &&
            value.data == nullptr && value.data_size == 0 &&
            value.offsets == nullptr && value.offsets_size == 0 && value.count == 0;
    };
    const auto makePayloadSentinelView = []()
    {
        return openusd_payload_arc_list_view{
            static_cast<uint32_t>(sizeof(openusd_payload_arc_list_view)),
            OPENUSD_PAYLOAD_ARC_LIST_VIEW_VERSION,
            reinterpret_cast<const char*>(uintptr_t{1}),
            29,
            reinterpret_cast<const size_t*>(uintptr_t{1}),
            31,
            37};
    };
    const auto isResetPayloadView = [](const openusd_payload_arc_list_view& value)
    {
        return value.struct_size == sizeof(openusd_payload_arc_list_view) &&
            value.version == OPENUSD_PAYLOAD_ARC_LIST_VIEW_VERSION &&
            value.data == nullptr && value.data_size == 0 &&
            value.offsets == nullptr && value.offsets_size == 0 && value.count == 0;
    };
    const auto isZeroMatrix = [](const openusd_matrix4d& value)
    {
        return std::all_of(
            std::begin(value.values),
            std::end(value.values),
            [](double item) { return item == 0.0; });
    };

    size_t count = 71;
    if (openusd_register_plugins(nullptr, &count, error) == OPENUSD_STATUS_OK || count != 0)
    {
        return false;
    }

    openusd_stage* stage = reinterpret_cast<openusd_stage*>(uintptr_t{1});
    if (openusd_stage_open(nullptr, &stage, error) == OPENUSD_STATUS_OK || stage != nullptr)
    {
        return false;
    }
    stage = reinterpret_cast<openusd_stage*>(uintptr_t{1});
    if (openusd_stage_create_new(nullptr, &stage, error) == OPENUSD_STATUS_OK ||
        stage != nullptr)
    {
        return false;
    }
    openusd_stage_access* access =
        reinterpret_cast<openusd_stage_access*>(uintptr_t{1});
    if (openusd_stage_access_begin(nullptr, &access, error) == OPENUSD_STATUS_OK ||
        access != nullptr)
    {
        return false;
    }
    openusd_layer* layer = reinterpret_cast<openusd_layer*>(uintptr_t{1});
    if (openusd_stage_get_root_layer(nullptr, &layer, error) == OPENUSD_STATUS_OK ||
        layer != nullptr)
    {
        return false;
    }
    layer = reinterpret_cast<openusd_layer*>(uintptr_t{1});
    if (openusd_stage_get_session_layer(nullptr, &layer, error) == OPENUSD_STATUS_OK ||
        layer != nullptr)
    {
        return false;
    }

    char stringValue[8] = {'x'};
    size_t required = 73;
    if (openusd_stage_get_root_layer_identifier(
            nullptr,
            stringValue,
            sizeof(stringValue),
            &required,
            error) == OPENUSD_STATUS_OK ||
        stringValue[0] != '\0' || required != 0)
    {
        return false;
    }

    double doubleValue = 79.0;
    if (openusd_stage_get_start_time_code(nullptr, &doubleValue, error) ==
            OPENUSD_STATUS_OK ||
        doubleValue != 0.0)
    {
        return false;
    }
    int32_t intValue = 83;
    if (openusd_stage_has_prim(nullptr, "/World", &intValue, error) ==
            OPENUSD_STATUS_OK ||
        intValue != 0)
    {
        return false;
    }
    uint64_t serial = 89;
    if (openusd_stage_get_change_serial(nullptr, &serial, error) ==
            OPENUSD_STATUS_OK ||
        serial != 0)
    {
        return false;
    }

    openusd_vec3f vec3Value{1.0F, 2.0F, 3.0F};
    if (openusd_stage_get_vec3f(
            nullptr, "/World", "custom:value", 0, 0, &vec3Value, error) ==
            OPENUSD_STATUS_OK ||
        vec3Value.x != 0.0F || vec3Value.y != 0.0F || vec3Value.z != 0.0F)
    {
        return false;
    }
    openusd_matrix4d matrixValue{};
    std::fill(std::begin(matrixValue.values), std::end(matrixValue.values), 97.0);
    if (openusd_stage_get_matrix4d(
            nullptr, "/World", "custom:value", 0, 0, &matrixValue, error) ==
            OPENUSD_STATUS_OK ||
        !isZeroMatrix(matrixValue))
    {
        return false;
    }
    openusd_extent3f extentValue{{1.0F, 2.0F, 3.0F}, {4.0F, 5.0F, 6.0F}};
    if (openusd_geom_mesh_get_extent(
            nullptr, "/World", 0, 0, &extentValue, error) == OPENUSD_STATUS_OK ||
        extentValue.minimum.x != 0.0F || extentValue.minimum.y != 0.0F ||
        extentValue.minimum.z != 0.0F || extentValue.maximum.x != 0.0F ||
        extentValue.maximum.y != 0.0F || extentValue.maximum.z != 0.0F)
    {
        return false;
    }
    openusd_bounds3d boundsValue{
        static_cast<uint32_t>(sizeof(openusd_bounds3d)),
        OPENUSD_BOUNDS3D_VERSION,
        17,
        0,
        {19.0, 23.0, 29.0},
        {31.0, 37.0, 41.0}};
    if (openusd_stage_get_world_bounds(
            nullptr,
            nullptr,
            OPENUSD_GEOM_PURPOSE_MASK_ALL,
            0,
            0,
            &boundsValue,
            error) == OPENUSD_STATUS_OK ||
        boundsValue.struct_size != sizeof(openusd_bounds3d) ||
        boundsValue.version != OPENUSD_BOUNDS3D_VERSION ||
        boundsValue.is_valid != 0 || boundsValue.is_empty != 1 ||
        !std::all_of(
            std::begin(boundsValue.minimum),
            std::end(boundsValue.minimum),
            [](double item) { return item == 0.0; }) ||
        !std::all_of(
            std::begin(boundsValue.maximum),
            std::end(boundsValue.maximum),
            [](double item) { return item == 0.0; }))
    {
        return false;
    }

    double arrayValue[1] = {101.0};
    required = 103;
    if (openusd_stage_get_double_array(
            nullptr,
            "/World",
            "custom:value",
            0,
            0,
            arrayValue,
            1,
            &required,
            error) == OPENUSD_STATUS_OK ||
        required != 0)
    {
        return false;
    }

    openusd_metadata_value metadataValue{
        static_cast<uint32_t>(sizeof(openusd_metadata_value)), 107, 109, 113, 127.0};
    stringValue[0] = 'x';
    required = 131;
    if (openusd_stage_get_prim_metadata(
            validStage,
            "relative",
            "kind",
            OPENUSD_METADATA_KIND_STRING,
            &metadataValue,
            stringValue,
            sizeof(stringValue),
            &required,
            error) == OPENUSD_STATUS_OK ||
        metadataValue.struct_size != sizeof(openusd_metadata_value) ||
        metadataValue.kind != 0 || metadataValue.bool_value != 0 ||
        metadataValue.int64_value != 0 || metadataValue.double_value != 0.0 ||
        stringValue[0] != '\0' || required != 0)
    {
        return false;
    }

    openusd_scalar_value scalarValue{};
    scalarValue.struct_size = static_cast<uint32_t>(sizeof(openusd_scalar_value));
    scalarValue.kind = 137;
    scalarValue.bool_value = 139;
    scalarValue.int64_value = 149;
    scalarValue.double_value = 151.0;
    scalarValue.vec3f_value = {157.0F, 163.0F, 167.0F};
    std::fill(
        std::begin(scalarValue.matrix4d_value.values),
        std::end(scalarValue.matrix4d_value.values),
        173.0);
    stringValue[0] = 'x';
    required = 179;
    if (openusd_stage_get_attribute_scalar_value(
            validStage,
            "relative",
            "custom:value",
            0,
            0,
            &scalarValue,
            stringValue,
            sizeof(stringValue),
            &required,
            error) == OPENUSD_STATUS_OK ||
        scalarValue.struct_size != sizeof(openusd_scalar_value) ||
        scalarValue.kind != 0 || scalarValue.bool_value != 0 ||
        scalarValue.int64_value != 0 || scalarValue.double_value != 0.0 ||
        scalarValue.vec3f_value.x != 0.0F ||
        !isZeroMatrix(scalarValue.matrix4d_value) ||
        stringValue[0] != '\0' || required != 0)
    {
        return false;
    }

    openusd_string_list* list =
        reinterpret_cast<openusd_string_list*>(uintptr_t{1});
    openusd_string_list_view view = makeSentinelView();
    if (openusd_stage_get_prim_paths(nullptr, &list, &view, error) ==
            OPENUSD_STATUS_OK ||
        list != nullptr || !isResetView(view))
    {
        return false;
    }
    list = reinterpret_cast<openusd_string_list*>(uintptr_t{1});
    view = makeSentinelView();
    if (openusd_stage_get_variant_set_names(
            validStage,
            "relative",
            &list,
            &view,
            error) == OPENUSD_STATUS_OK ||
        list != nullptr || !isResetView(view))
    {
        return false;
    }
    openusd_payload_arc_list* payloadList =
        reinterpret_cast<openusd_payload_arc_list*>(uintptr_t{1});
    openusd_payload_arc_list_view payloadView = makePayloadSentinelView();
    if (openusd_stage_get_composed_payload_arcs(
            validStage,
            "relative",
            &payloadList,
            &payloadView,
            error) == OPENUSD_STATUS_OK ||
        payloadList != nullptr || !isResetPayloadView(payloadView))
    {
        return false;
    }

    intValue = 181;
    if (openusd_geom_is_schema(
            validStage, "relative", OPENUSD_GEOM_SCHEMA_MESH, &intValue, error) ==
            OPENUSD_STATUS_OK ||
        intValue != 0)
    {
        return false;
    }
    openusd_shade_value_type valueType =
        static_cast<openusd_shade_value_type>(191);
    if (openusd_shade_get_output_type(
            validStage, "relative", "rgb", &valueType, error) ==
            OPENUSD_STATUS_OK ||
        valueType != OPENUSD_SHADE_VALUE_INVALID)
    {
        return false;
    }
    vec3Value = {193.0F, 197.0F, 199.0F};
    if (openusd_lux_get_color(validStage, "relative", &vec3Value, error) ==
            OPENUSD_STATUS_OK ||
        vec3Value.x != 0.0F || vec3Value.y != 0.0F || vec3Value.z != 0.0F)
    {
        return false;
    }

    openusd_shade_attribute_type sourceType =
        static_cast<openusd_shade_attribute_type>(211);
    list = reinterpret_cast<openusd_string_list*>(uintptr_t{1});
    view = makeSentinelView();
    if (openusd_shade_get_connected_source(
            validStage,
            "relative",
            "value",
            OPENUSD_SHADE_ATTRIBUTE_INPUT,
            &list,
            &view,
            &sourceType,
            error) == OPENUSD_STATUS_OK ||
        list != nullptr || !isResetView(view) ||
        sourceType != OPENUSD_SHADE_ATTRIBUTE_INVALID)
    {
        return false;
    }
    list = reinterpret_cast<openusd_string_list*>(uintptr_t{1});
    view = makeSentinelView();
    if (openusd_shade_get_connected_sources(
            validStage,
            "relative",
            "value",
            OPENUSD_SHADE_ATTRIBUTE_INPUT,
            &list,
            &view,
            error) == OPENUSD_STATUS_OK ||
        list != nullptr || !isResetView(view))
    {
        return false;
    }

    list = reinterpret_cast<openusd_string_list*>(uintptr_t{1});
    view = makeSentinelView();
    if (openusd_skel_get_joints(
            validStage,
            "relative",
            OPENUSD_SKEL_SCHEMA_SKELETON,
            &list,
            &view,
            error) == OPENUSD_STATUS_OK ||
        list != nullptr || !isResetView(view))
    {
        return false;
    }
    required = 223;
    if (openusd_skel_get_skeleton_matrices(
            validStage,
            "relative",
            OPENUSD_SKEL_MATRIX_BIND_TRANSFORMS,
            &matrixValue,
            1,
            &required,
            error) == OPENUSD_STATUS_OK ||
        required != 0)
    {
        return false;
    }
    openusd_quatf quatValue{227.0F, 229.0F, 233.0F, 239.0F};
    required = 241;
    if (openusd_skel_get_animation_rotations(
            validStage,
            "relative",
            0,
            0,
            &quatValue,
            1,
            &required,
            error) == OPENUSD_STATUS_OK ||
        required != 0)
    {
        return false;
    }
    stringValue[0] = 'x';
    required = 251;
    if (openusd_skel_get_binding_target(
            validStage,
            "relative",
            OPENUSD_SKEL_BINDING_SKELETON,
            stringValue,
            sizeof(stringValue),
            &required,
            error) == OPENUSD_STATUS_OK ||
        stringValue[0] != '\0' || required != 0)
    {
        return false;
    }
    std::fill(std::begin(matrixValue.values), std::end(matrixValue.values), 257.0);
    if (openusd_skel_get_geom_bind_transform(
            validStage, "relative", &matrixValue, error) == OPENUSD_STATUS_OK ||
        !isZeroMatrix(matrixValue))
    {
        return false;
    }

    int32_t indices[1] = {263};
    float weights[1] = {269.0F};
    size_t indexRequired = 271;
    size_t weightRequired = 277;
    int32_t elementSize = 281;
    openusd_skel_interpolation interpolation =
        static_cast<openusd_skel_interpolation>(283);
    if (openusd_skel_get_joint_influences(
            validStage,
            "relative",
            indices,
            1,
            &indexRequired,
            weights,
            1,
            &weightRequired,
            &elementSize,
            &interpolation,
            error) == OPENUSD_STATUS_OK ||
        indexRequired != 0 || weightRequired != 0 || elementSize != 0 ||
        interpolation != OPENUSD_SKEL_INTERPOLATION_CONSTANT)
    {
        return false;
    }

    return true;
}

bool VerifySharedStageAccess(
    openusd_stage* stage,
    const char* stagePath,
    openusd_error_buffer* error)
{
    const size_t initialLiveStageCores = openusd_test_get_live_stage_core_count();
    if (openusd_stage_retain(stage, error) != OPENUSD_STATUS_OK)
    {
        return false;
    }
    openusd_stage_release(stage);
    if (openusd_test_get_live_stage_core_count() != initialLiveStageCores)
    {
        return false;
    }

    for (const char* failpoint : {"after-retain", "after-lock"})
    {
        const size_t liveBefore = openusd_test_get_live_stage_core_count();
        const size_t destroyedBefore = openusd_test_get_destroyed_stage_core_count();
        openusd_stage* failpointStage = nullptr;
        if (openusd_stage_open(stagePath, &failpointStage, error) != OPENUSD_STATUS_OK ||
            openusd_test_get_live_stage_core_count() != liveBefore + 1)
        {
            openusd_stage_release(failpointStage);
            return false;
        }

        SetStageAccessBeginFailpoint(failpoint);
        openusd_stage_access* failedAccess =
            reinterpret_cast<openusd_stage_access*>(uintptr_t{1});
        const openusd_status failedStatus =
            openusd_stage_access_begin(failpointStage, &failedAccess, error);
        SetStageAccessBeginFailpoint(nullptr);
        openusd_stage_release(failpointStage);
        if (failedStatus != OPENUSD_STATUS_NATIVE_ERROR || failedAccess != nullptr ||
            openusd_test_get_live_stage_core_count() != liveBefore ||
            openusd_test_get_destroyed_stage_core_count() != destroyedBefore + 1)
        {
            return false;
        }
    }

    openusd_stage_access* access = nullptr;
    if (openusd_stage_access_begin(stage, &access, error) != OPENUSD_STATUS_OK ||
        access == nullptr)
    {
        return false;
    }

    uint64_t serialBeforeMutation = std::numeric_limits<uint64_t>::max();
    if (openusd_stage_get_change_serial(stage, &serialBeforeMutation, error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_define_prim(
            stage,
            "/__SharedStageNoticeDelivery",
            "Xform",
            error) != OPENUSD_STATUS_OK)
    {
        openusd_stage_access_end(access, error);
        return false;
    }
    uint64_t serialAfterMutation = 0;
    if (openusd_stage_get_change_serial(stage, &serialAfterMutation, error) !=
            OPENUSD_STATUS_OK ||
        serialAfterMutation <= serialBeforeMutation)
    {
        openusd_stage_access_end(access, error);
        return false;
    }

    std::atomic<openusd_status> wrongThreadStatus{OPENUSD_STATUS_OK};
    std::thread wrongThread([&]()
    {
        std::array<char, 256> localText{};
        openusd_error_buffer localError{localText.data(), localText.size(), 0};
        wrongThreadStatus.store(
            openusd_stage_access_end(access, &localError),
            std::memory_order_release);
    });
    wrongThread.join();
    if (wrongThreadStatus.load(std::memory_order_acquire) !=
        OPENUSD_STATUS_WRONG_THREAD)
    {
        openusd_stage_access_end(access, error);
        return false;
    }

    if (openusd_stage_access_end(access, error) != OPENUSD_STATUS_OK)
    {
        return false;
    }

    constexpr size_t blockingIterations = 32;
    for (size_t index = 0; index < blockingIterations; ++index)
    {
        access = nullptr;
        if (openusd_stage_access_begin(stage, &access, error) != OPENUSD_STATUS_OK)
        {
            return false;
        }

        ThreadBarrier barrier(2);
        std::atomic<bool> entered{false};
        std::atomic<bool> completed{false};
        std::atomic<openusd_status> concurrentStatus{OPENUSD_STATUS_NATIVE_ERROR};
        const std::string primPath = "/__SharedStageBlocking/Prim" + std::to_string(index);
        std::thread concurrent([&, index]()
        {
            std::array<char, 256> localText{};
            openusd_error_buffer localError{localText.data(), localText.size(), 0};
            barrier.ArriveAndWait();
            entered.store(true, std::memory_order_release);
            if ((index & 1U) == 0)
            {
                uint64_t localSerial = 0;
                concurrentStatus.store(
                    openusd_stage_get_change_serial(stage, &localSerial, &localError),
                    std::memory_order_release);
            }
            else
            {
                concurrentStatus.store(
                    openusd_stage_define_prim(
                        stage,
                        primPath.c_str(),
                        "Xform",
                        &localError),
                    std::memory_order_release);
            }
            completed.store(true, std::memory_order_release);
        });
        barrier.ArriveAndWait();
        while (!entered.load(std::memory_order_acquire))
        {
            std::this_thread::yield();
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(2));
        const bool completedWhileLocked = completed.load(std::memory_order_acquire);
        const openusd_status endStatus = openusd_stage_access_end(access, error);
        concurrent.join();
        if (completedWhileLocked || endStatus != OPENUSD_STATUS_OK ||
            !completed.load(std::memory_order_acquire) ||
            concurrentStatus.load(std::memory_order_acquire) != OPENUSD_STATUS_OK)
        {
            return false;
        }
    }

    openusd_layer* rootLayer = nullptr;
    if (openusd_stage_get_root_layer(stage, &rootLayer, error) != OPENUSD_STATUS_OK)
    {
        return false;
    }
    for (size_t index = 0; index < blockingIterations; ++index)
    {
        access = nullptr;
        if (openusd_stage_access_begin(stage, &access, error) != OPENUSD_STATUS_OK)
        {
            openusd_layer_release(rootLayer);
            return false;
        }

        ThreadBarrier barrier(2);
        std::atomic<bool> entered{false};
        std::atomic<bool> completed{false};
        std::atomic<openusd_status> layerStatus{OPENUSD_STATUS_NATIVE_ERROR};
        std::thread concurrent([&, index]()
        {
            std::array<char, 256> localText{};
            openusd_error_buffer localError{localText.data(), localText.size(), 0};
            openusd_metadata_value value{
                static_cast<uint32_t>(sizeof(openusd_metadata_value)),
                OPENUSD_METADATA_KIND_INT64,
                0,
                static_cast<int64_t>(index),
                0.0};
            barrier.ArriveAndWait();
            entered.store(true, std::memory_order_release);
            layerStatus.store(
                openusd_layer_set_metadata(
                    rootLayer,
                    "sharedStageBlocking",
                    &value,
                    nullptr,
                    &localError),
                std::memory_order_release);
            completed.store(true, std::memory_order_release);
        });
        barrier.ArriveAndWait();
        while (!entered.load(std::memory_order_acquire))
        {
            std::this_thread::yield();
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(2));
        const bool completedWhileLocked = completed.load(std::memory_order_acquire);
        const openusd_status endStatus = openusd_stage_access_end(access, error);
        concurrent.join();
        if (completedWhileLocked || endStatus != OPENUSD_STATUS_OK ||
            layerStatus.load(std::memory_order_acquire) != OPENUSD_STATUS_OK)
        {
            openusd_layer_release(rootLayer);
            return false;
        }
    }
    openusd_layer_release(rootLayer);

    openusd_stage* layerLifetimeStage = nullptr;
    openusd_layer* lifetimeLayer = nullptr;
    const size_t layerLiveBefore = openusd_test_get_live_stage_core_count();
    const size_t layerDestroyedBefore = openusd_test_get_destroyed_stage_core_count();
    if (openusd_stage_open(stagePath, &layerLifetimeStage, error) != OPENUSD_STATUS_OK ||
        openusd_stage_get_root_layer(layerLifetimeStage, &lifetimeLayer, error) !=
            OPENUSD_STATUS_OK ||
        openusd_test_get_live_stage_core_count() != layerLiveBefore + 1)
    {
        openusd_stage_release(layerLifetimeStage);
        return false;
    }
    openusd_stage_release(layerLifetimeStage);
    std::array<char, 1024> layerIdentifier{};
    size_t layerIdentifierRequired = 0;
    if (openusd_layer_get_identifier(
            lifetimeLayer,
            layerIdentifier.data(),
            layerIdentifier.size(),
            &layerIdentifierRequired,
            error) != OPENUSD_STATUS_OK ||
        layerIdentifierRequired == 0)
    {
        openusd_layer_release(lifetimeLayer);
        return false;
    }
    openusd_metadata_value lifetimeMetadata{
        static_cast<uint32_t>(sizeof(openusd_metadata_value)),
        OPENUSD_METADATA_KIND_BOOL,
        1,
        0,
        0.0};
    if (openusd_layer_set_metadata(
            lifetimeLayer,
            "layerAfterStageRelease",
            &lifetimeMetadata,
            nullptr,
            error) != OPENUSD_STATUS_OK)
    {
        openusd_layer_release(lifetimeLayer);
        return false;
    }
    openusd_layer_release(lifetimeLayer);
    if (openusd_test_get_live_stage_core_count() != layerLiveBefore ||
        openusd_test_get_destroyed_stage_core_count() != layerDestroyedBefore + 1)
    {
        return false;
    }

    openusd_stage* lifetimeStage = nullptr;
    const size_t accessLiveBefore = openusd_test_get_live_stage_core_count();
    const size_t accessDestroyedBefore = openusd_test_get_destroyed_stage_core_count();
    if (openusd_stage_open(stagePath, &lifetimeStage, error) != OPENUSD_STATUS_OK)
    {
        return false;
    }
    openusd_stage_access* lifetimeAccess = nullptr;
    if (openusd_stage_access_begin(lifetimeStage, &lifetimeAccess, error) !=
        OPENUSD_STATUS_OK)
    {
        openusd_stage_release(lifetimeStage);
        return false;
    }
    uint64_t lifetimeSerialBefore = 0;
    if (openusd_stage_get_change_serial(
            lifetimeStage,
            &lifetimeSerialBefore,
            error) != OPENUSD_STATUS_OK)
    {
        openusd_stage_access_end(lifetimeAccess, error);
        openusd_stage_release(lifetimeStage);
        return false;
    }
    openusd_stage_release(lifetimeStage);
    if (openusd_stage_define_prim(
            lifetimeStage,
            "/__SharedStageAccessLifetime",
            "Xform",
            error) != OPENUSD_STATUS_OK)
    {
        openusd_stage_access_end(lifetimeAccess, error);
        return false;
    }
    uint64_t lifetimeSerialAfter = 0;
    if (openusd_stage_get_change_serial(
            lifetimeStage,
            &lifetimeSerialAfter,
            error) != OPENUSD_STATUS_OK ||
        lifetimeSerialAfter <= lifetimeSerialBefore)
    {
        openusd_stage_access_end(lifetimeAccess, error);
        return false;
    }
    if (openusd_stage_access_end(lifetimeAccess, error) != OPENUSD_STATUS_OK)
    {
        return false;
    }
    if (openusd_test_get_live_stage_core_count() != accessLiveBefore ||
        openusd_test_get_destroyed_stage_core_count() != accessDestroyedBefore + 1)
    {
        return false;
    }

    openusd_stage* stormStage = nullptr;
    const size_t stormLiveBefore = openusd_test_get_live_stage_core_count();
    const size_t stormDestroyedBefore = openusd_test_get_destroyed_stage_core_count();
    if (openusd_stage_open(stagePath, &stormStage, error) != OPENUSD_STATUS_OK)
    {
        return false;
    }
    constexpr size_t stormThreadCount = 8;
    constexpr size_t stormIterations = 1000;
    for (size_t index = 0; index < stormThreadCount; ++index)
    {
        if (openusd_stage_retain(stormStage, error) != OPENUSD_STATUS_OK)
        {
            openusd_stage_release(stormStage);
            return false;
        }
    }

    ThreadBarrier releaseBarrier(stormThreadCount + 1);
    std::atomic<bool> stormFailed{false};
    std::vector<std::thread> stormThreads;
    stormThreads.reserve(stormThreadCount);
    for (size_t threadIndex = 0; threadIndex < stormThreadCount; ++threadIndex)
    {
        stormThreads.emplace_back([&]()
        {
            std::array<char, 256> localText{};
            openusd_error_buffer localError{localText.data(), localText.size(), 0};
            for (size_t iteration = 0; iteration < stormIterations; ++iteration)
            {
                if (openusd_stage_retain(stormStage, &localError) != OPENUSD_STATUS_OK)
                {
                    stormFailed.store(true, std::memory_order_release);
                    break;
                }
                openusd_stage_release(stormStage);
            }
            releaseBarrier.ArriveAndWait();
            openusd_stage_release(stormStage);
        });
    }
    releaseBarrier.ArriveAndWait();
    openusd_stage_release(stormStage);
    for (std::thread& thread : stormThreads)
    {
        thread.join();
    }
    return !stormFailed.load(std::memory_order_acquire) &&
        openusd_test_get_live_stage_core_count() == stormLiveBefore &&
        openusd_test_get_destroyed_stage_core_count() == stormDestroyedBefore + 1;
}

bool VerifyWritableBufferSentinels(
    const openusd_stage* stage,
    openusd_error_buffer* error)
{
    const char* primPath = "/__BufferSentinels";
    const char* meshPath = "/__BufferSentinels/Mesh";
    const char* rootPath = "/__BufferSentinels/Character";
    const char* skeletonPath = "/__BufferSentinels/Character/Skeleton";
    const char* animationPath = "/__BufferSentinels/Character/Animation";

    const std::array<double, 2> doubles{1.0, 2.0};
    const std::array<int32_t, 2> ints{1, 2};
    const std::array<float, 2> floats{1.0F, 2.0F};
    const std::array<openusd_vec2f, 2> vec2s{
        openusd_vec2f{1.0F, 2.0F},
        openusd_vec2f{3.0F, 4.0F}};
    const std::array<openusd_vec3f, 2> vec3s{
        openusd_vec3f{1.0F, 2.0F, 3.0F},
        openusd_vec3f{4.0F, 5.0F, 6.0F}};
    const std::array<int32_t, 2> faceCounts{1, 1};
    const std::array<int32_t, 2> faceIndices{0, 1};
    const std::array<openusd_matrix4d, 2> matrices{
        openusd_matrix4d{{1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1}},
        openusd_matrix4d{{1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 1, 0, 1}}};
    const std::array<openusd_quatf, 2> rotations{
        openusd_quatf{1.0F, 0.0F, 0.0F, 0.0F},
        openusd_quatf{1.0F, 0.0F, 0.0F, 0.0F}};
    const std::array<int32_t, 2> influenceIndices{0, 1};
    const std::array<float, 2> influenceWeights{0.5F, 0.5F};
    const std::array<char, 16> jointData{
        'R', 'o', 'o', 't', '\0',
        'R', 'o', 'o', 't', '/', 'C', 'h', 'i', 'l', 'd', '\0'};
    const std::array<size_t, 2> jointOffsets{0, 5};
    const openusd_string_list_view joints{
        static_cast<uint32_t>(sizeof(openusd_string_list_view)),
        jointData.data(),
        jointData.size(),
        jointOffsets.data(),
        jointOffsets.size() * sizeof(size_t),
        jointOffsets.size()};

    if (openusd_stage_define_prim(stage, primPath, "Xform", error) != OPENUSD_STATUS_OK ||
        openusd_stage_set_double(
            stage, primPath, "sentinel:sample", 1.0, 1, 1.0, error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_double(
            stage, primPath, "sentinel:sample", 2.0, 1, 2.0, error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_double_array(
            stage, primPath, "sentinel:doubles", doubles.data(), doubles.size(), 0, 0, error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_int32_array(
            stage, primPath, "sentinel:ints", ints.data(), ints.size(), 0, 0, error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_float_array(
            stage, primPath, "sentinel:floats", floats.data(), floats.size(), 0, 0, error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_vec2f_array(
            stage, primPath, "sentinel:vec2s", vec2s.data(), vec2s.size(), 0, 0, error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_vec3f_array(
            stage, primPath, "sentinel:vec3s", vec3s.data(), vec3s.size(), 0, 0, error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_define_mesh(stage, meshPath, error) != OPENUSD_STATUS_OK ||
        openusd_geom_mesh_set_points(
            stage, meshPath, vec3s.data(), vec3s.size(), 0, 0, error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_mesh_set_topology(
            stage,
            meshPath,
            faceCounts.data(),
            faceCounts.size(),
            faceIndices.data(),
            faceIndices.size(),
            error) != OPENUSD_STATUS_OK ||
        openusd_geom_mesh_set_normals(
            stage,
            meshPath,
            vec3s.data(),
            vec3s.size(),
            OPENUSD_GEOM_INTERPOLATION_VERTEX,
            0,
            0,
            error) != OPENUSD_STATUS_OK ||
        openusd_skel_define(stage, rootPath, OPENUSD_SKEL_SCHEMA_ROOT, error) !=
            OPENUSD_STATUS_OK ||
        openusd_skel_define(stage, skeletonPath, OPENUSD_SKEL_SCHEMA_SKELETON, error) !=
            OPENUSD_STATUS_OK ||
        openusd_skel_define(stage, animationPath, OPENUSD_SKEL_SCHEMA_ANIMATION, error) !=
            OPENUSD_STATUS_OK ||
        openusd_skel_set_joints(
            stage, skeletonPath, OPENUSD_SKEL_SCHEMA_SKELETON, &joints, error) !=
            OPENUSD_STATUS_OK ||
        openusd_skel_set_joints(
            stage, animationPath, OPENUSD_SKEL_SCHEMA_ANIMATION, &joints, error) !=
            OPENUSD_STATUS_OK ||
        openusd_skel_set_skeleton_matrices(
            stage,
            skeletonPath,
            OPENUSD_SKEL_MATRIX_BIND_TRANSFORMS,
            matrices.data(),
            matrices.size(),
            error) != OPENUSD_STATUS_OK ||
        openusd_skel_set_animation_vec3(
            stage,
            animationPath,
            OPENUSD_SKEL_ANIMATION_TRANSLATIONS,
            vec3s.data(),
            vec3s.size(),
            0,
            0,
            error) != OPENUSD_STATUS_OK ||
        openusd_skel_set_animation_rotations(
            stage, animationPath, rotations.data(), rotations.size(), 0, 0, error) !=
            OPENUSD_STATUS_OK ||
        openusd_skel_apply_binding(stage, meshPath, error) != OPENUSD_STATUS_OK ||
        openusd_skel_set_binding_target(
            stage,
            meshPath,
            OPENUSD_SKEL_BINDING_SKELETON,
            skeletonPath,
            error) != OPENUSD_STATUS_OK ||
        openusd_skel_set_joint_influences(
            stage,
            meshPath,
            influenceIndices.data(),
            influenceIndices.size(),
            influenceWeights.data(),
            influenceWeights.size(),
            2,
            OPENUSD_SKEL_INTERPOLATION_CONSTANT,
            error) != OPENUSD_STATUS_OK)
    {
        std::cerr << "Writable-buffer sentinel setup failed.\n";
        return false;
    }

    const auto timeSamples = [&](const openusd_stage* inputStage,
                                 const char* path,
                                 double* values,
                                 size_t capacity,
                                 size_t* required,
                                 openusd_error_buffer* outputError)
    {
        return openusd_stage_get_attribute_time_samples(
            inputStage, path, "sentinel:sample", values, capacity, required, outputError);
    };
    const auto doubleArrays = [&](const openusd_stage* inputStage,
                                  const char* path,
                                  double* values,
                                  size_t capacity,
                                  size_t* required,
                                  openusd_error_buffer* outputError)
    {
        return openusd_stage_get_double_array(
            inputStage,
            path,
            "sentinel:doubles",
            0,
            0,
            values,
            capacity,
            required,
            outputError);
    };
    const auto intArrays = [&](const openusd_stage* inputStage,
                               const char* path,
                               int32_t* values,
                               size_t capacity,
                               size_t* required,
                               openusd_error_buffer* outputError)
    {
        return openusd_stage_get_int32_array(
            inputStage, path, "sentinel:ints", 0, 0, values, capacity, required, outputError);
    };
    const auto floatArrays = [&](const openusd_stage* inputStage,
                                 const char* path,
                                 float* values,
                                 size_t capacity,
                                 size_t* required,
                                 openusd_error_buffer* outputError)
    {
        return openusd_stage_get_float_array(
            inputStage, path, "sentinel:floats", 0, 0, values, capacity, required, outputError);
    };
    const auto vec2Arrays = [&](const openusd_stage* inputStage,
                                const char* path,
                                openusd_vec2f* values,
                                size_t capacity,
                                size_t* required,
                                openusd_error_buffer* outputError)
    {
        return openusd_stage_get_vec2f_array(
            inputStage, path, "sentinel:vec2s", 0, 0, values, capacity, required, outputError);
    };
    const auto vec3Arrays = [&](const openusd_stage* inputStage,
                                const char* path,
                                openusd_vec3f* values,
                                size_t capacity,
                                size_t* required,
                                openusd_error_buffer* outputError)
    {
        return openusd_stage_get_vec3f_array(
            inputStage, path, "sentinel:vec3s", 0, 0, values, capacity, required, outputError);
    };
    const auto meshPoints = [&](const openusd_stage* inputStage,
                                const char* path,
                                openusd_vec3f* values,
                                size_t capacity,
                                size_t* required,
                                openusd_error_buffer* outputError)
    {
        return openusd_geom_mesh_get_points(
            inputStage, path, 0, 0, values, capacity, required, outputError);
    };
    const auto faceVertexCounts = [&](const openusd_stage* inputStage,
                                      const char* path,
                                      int32_t* values,
                                      size_t capacity,
                                      size_t* required,
                                      openusd_error_buffer* outputError)
    {
        return openusd_geom_mesh_get_face_vertex_counts(
            inputStage, path, values, capacity, required, outputError);
    };
    const auto faceVertexIndices = [&](const openusd_stage* inputStage,
                                       const char* path,
                                       int32_t* values,
                                       size_t capacity,
                                       size_t* required,
                                       openusd_error_buffer* outputError)
    {
        return openusd_geom_mesh_get_face_vertex_indices(
            inputStage, path, values, capacity, required, outputError);
    };
    const auto meshNormals = [&](const openusd_stage* inputStage,
                                 const char* path,
                                 openusd_vec3f* values,
                                 size_t capacity,
                                 size_t* required,
                                 openusd_error_buffer* outputError)
    {
        return openusd_geom_mesh_get_normals(
            inputStage, path, 0, 0, values, capacity, required, outputError);
    };
    const auto skeletonMatrices = [&](const openusd_stage* inputStage,
                                      const char* path,
                                      openusd_matrix4d* values,
                                      size_t capacity,
                                      size_t* required,
                                      openusd_error_buffer* outputError)
    {
        return openusd_skel_get_skeleton_matrices(
            inputStage,
            path,
            OPENUSD_SKEL_MATRIX_BIND_TRANSFORMS,
            values,
            capacity,
            required,
            outputError);
    };
    const auto animationVec3 = [&](const openusd_stage* inputStage,
                                   const char* path,
                                   openusd_vec3f* values,
                                   size_t capacity,
                                   size_t* required,
                                   openusd_error_buffer* outputError)
    {
        return openusd_skel_get_animation_vec3(
            inputStage,
            path,
            OPENUSD_SKEL_ANIMATION_TRANSLATIONS,
            0,
            0,
            values,
            capacity,
            required,
            outputError);
    };
    const auto animationRotations = [&](const openusd_stage* inputStage,
                                        const char* path,
                                        openusd_quatf* values,
                                        size_t capacity,
                                        size_t* required,
                                        openusd_error_buffer* outputError)
    {
        return openusd_skel_get_animation_rotations(
            inputStage, path, 0, 0, values, capacity, required, outputError);
    };

    const auto verify = [](bool result, const char* label)
    {
        if (!result)
        {
            std::cerr << "Writable-buffer sentinel failure: " << label << '\n';
        }
        return result;
    };
    if (!verify(
            VerifyWritableArrayGetter<double>(stage, primPath, 2, timeSamples, error),
            "time samples") ||
        !verify(
            VerifyWritableArrayGetter<double>(stage, primPath, 2, doubleArrays, error),
            "double arrays") ||
        !verify(
            VerifyWritableArrayGetter<int32_t>(stage, primPath, 2, intArrays, error),
            "int32 arrays") ||
        !verify(
            VerifyWritableArrayGetter<float>(stage, primPath, 2, floatArrays, error),
            "float arrays") ||
        !verify(
            VerifyWritableArrayGetter<openusd_vec2f>(stage, primPath, 2, vec2Arrays, error),
            "vec2f arrays") ||
        !verify(
            VerifyWritableArrayGetter<openusd_vec3f>(stage, primPath, 2, vec3Arrays, error),
            "vec3f arrays") ||
        !verify(
            VerifyWritableArrayGetter<openusd_vec3f>(stage, meshPath, 2, meshPoints, error),
            "mesh points") ||
        !verify(
            VerifyWritableArrayGetter<int32_t>(
                stage, meshPath, 2, faceVertexCounts, error),
            "face-vertex counts") ||
        !verify(
            VerifyWritableArrayGetter<int32_t>(
                stage, meshPath, 2, faceVertexIndices, error),
            "face-vertex indices") ||
        !verify(
            VerifyWritableArrayGetter<openusd_vec3f>(
                stage, meshPath, 2, meshNormals, error),
            "mesh normals") ||
        !verify(
            VerifyWritableArrayGetter<openusd_matrix4d>(
                stage, skeletonPath, 2, skeletonMatrices, error),
            "skeleton matrices") ||
        !verify(
            VerifyWritableArrayGetter<openusd_vec3f>(
                stage, animationPath, 2, animationVec3, error),
            "animation vec3") ||
        !verify(
            VerifyWritableArrayGetter<openusd_quatf>(
                stage, animationPath, 2, animationRotations, error),
            "animation rotations"))
    {
        return false;
    }

    const auto verifyInfluenceFailure =
        [&](const openusd_stage* inputStage,
            const char* path,
            openusd_status expectedStatus)
        {
            std::array<int32_t, 3> indices;
            std::array<float, 3> weights;
            std::memset(indices.data(), 0xa5, sizeof(indices));
            std::memset(weights.data(), 0xa5, sizeof(weights));
            size_t indexRequired = 31337;
            size_t weightRequired = 31337;
            int32_t elementSize = 31337;
            openusd_skel_interpolation interpolation =
                static_cast<openusd_skel_interpolation>(31337);
            const openusd_status status = openusd_skel_get_joint_influences(
                inputStage,
                path,
                indices.data(),
                indices.size(),
                &indexRequired,
                weights.data(),
                weights.size(),
                &weightRequired,
                &elementSize,
                &interpolation,
                error);
            return status == expectedStatus && indexRequired == 0 && weightRequired == 0 &&
                elementSize == 0 && interpolation == OPENUSD_SKEL_INTERPOLATION_CONSTANT &&
                IsZeroed(indices.data(), indices.size()) &&
                IsZeroed(weights.data(), weights.size());
        };
    if (!verifyInfluenceFailure(nullptr, meshPath, OPENUSD_STATUS_INVALID_ARGUMENT) ||
        !verifyInfluenceFailure(
            stage, "/__BufferSentinels/Missing", OPENUSD_STATUS_NOT_FOUND))
    {
        std::cerr << "Writable-buffer sentinel failure: joint influence failure paths\n";
        return false;
    }

    size_t indexRequired = 31337;
    size_t weightRequired = 31337;
    int32_t elementSize = 31337;
    openusd_skel_interpolation interpolation =
        static_cast<openusd_skel_interpolation>(31337);
    if (openusd_skel_get_joint_influences(
            stage,
            meshPath,
            nullptr,
            0,
            &indexRequired,
            nullptr,
            0,
            &weightRequired,
            &elementSize,
            &interpolation,
            error) != OPENUSD_STATUS_OK ||
        indexRequired != 2 || weightRequired != 2 || elementSize != 2 ||
        interpolation != OPENUSD_SKEL_INTERPOLATION_CONSTANT)
    {
        std::cerr << "Writable-buffer sentinel failure: joint influence query\n";
        return false;
    }

    std::array<int32_t, 1> smallIndices;
    std::array<float, 1> smallWeights;
    std::memset(smallIndices.data(), 0xa5, sizeof(smallIndices));
    std::memset(smallWeights.data(), 0xa5, sizeof(smallWeights));
    indexRequired = 31337;
    weightRequired = 31337;
    elementSize = 31337;
    interpolation = static_cast<openusd_skel_interpolation>(31337);
    if (openusd_skel_get_joint_influences(
            stage,
            meshPath,
            smallIndices.data(),
            smallIndices.size(),
            &indexRequired,
            smallWeights.data(),
            smallWeights.size(),
            &weightRequired,
            &elementSize,
            &interpolation,
            error) != OPENUSD_STATUS_BUFFER_TOO_SMALL ||
        indexRequired != 0 || weightRequired != 0 || elementSize != 0 ||
        interpolation != OPENUSD_SKEL_INTERPOLATION_CONSTANT ||
        !IsZeroed(smallIndices.data(), smallIndices.size()) ||
        !IsZeroed(smallWeights.data(), smallWeights.size()))
    {
        std::cerr << "Writable-buffer sentinel failure: joint influence capacity\n";
        return false;
    }

    std::array<double, 1> overflowBuffer{17.0};
    size_t overflowRequired = 31337;
    const size_t overflowCapacity =
        std::numeric_limits<size_t>::max() / sizeof(double) + 1;
    const openusd_status overflowStatus = openusd_stage_get_double_array(
               stage,
               primPath,
               "sentinel:doubles",
               0,
               0,
               overflowBuffer.data(),
               overflowCapacity,
               &overflowRequired,
               error);
    const bool overflowSafe = overflowStatus == OPENUSD_STATUS_INVALID_ARGUMENT &&
        overflowRequired == 0 && overflowBuffer[0] == 17.0;
    if (!overflowSafe)
    {
        std::cerr << "Writable-buffer sentinel failure: overflowing capacity status=" <<
            overflowStatus << " required=" << overflowRequired <<
            " value=" << overflowBuffer[0] << '\n';
    }
    return overflowSafe;
}

bool ReadStageString(
    const openusd_stage* stage,
    StageStringGetter getter,
    openusd_error_buffer* error,
    std::string* value)
{
    size_t required = 0;
    openusd_status status = getter(stage, nullptr, 0, &required, error);
    if (status != OPENUSD_STATUS_BUFFER_TOO_SMALL || required == 0)
    {
        return false;
    }
    std::vector<char> buffer(required);
    status = getter(stage, buffer.data(), buffer.size(), &required, error);
    if (status != OPENUSD_STATUS_OK)
    {
        return false;
    }
    *value = buffer.data();
    return true;
}

bool ReadStageStringList(
    const openusd_stage* stage,
    StageStringListGetter getter,
    openusd_error_buffer* error,
    std::vector<std::string>* values)
{
    openusd_string_list* list = nullptr;
    openusd_string_list_view view{
        static_cast<uint32_t>(sizeof(openusd_string_list_view)), nullptr, 0, nullptr, 0, 0};
    const openusd_status status = getter(stage, &list, &view, error);
    if (status != OPENUSD_STATUS_OK)
    {
        return false;
    }

    values->clear();
    values->reserve(view.count);
    for (size_t i = 0; i < view.count; ++i)
    {
        values->emplace_back(view.data + view.offsets[i]);
    }
    openusd_string_list_release(list);
    return true;
}

bool ReadPrimString(
    const openusd_stage* stage,
    const char* primPath,
    PrimStringGetter getter,
    openusd_error_buffer* error,
    std::string* value)
{
    size_t required = 0;
    openusd_status status = getter(stage, primPath, nullptr, 0, &required, error);
    if (status != OPENUSD_STATUS_BUFFER_TOO_SMALL || required == 0)
    {
        return false;
    }
    std::vector<char> buffer(required);
    status = getter(stage, primPath, buffer.data(), buffer.size(), &required, error);
    if (status != OPENUSD_STATUS_OK)
    {
        return false;
    }
    *value = buffer.data();
    return true;
}

bool ReadScalar(
    const openusd_stage* stage,
    const char* primPath,
    const char* attributeName,
    int32_t timeSampled,
    double timeCode,
    openusd_error_buffer* error,
    openusd_scalar_value* value,
    std::string* text)
{
    *value = {};
    value->struct_size = static_cast<uint32_t>(sizeof(openusd_scalar_value));
    size_t required = 0;
    openusd_status status = openusd_stage_get_attribute_scalar_value(
        stage,
        primPath,
        attributeName,
        timeSampled,
        timeCode,
        value,
        nullptr,
        0,
        &required,
        error);
    if (status == OPENUSD_STATUS_OK)
    {
        text->clear();
        return true;
    }

    if (status != OPENUSD_STATUS_BUFFER_TOO_SMALL || required == 0)
    {
        return false;
    }

    std::vector<char> buffer(required);
    status = openusd_stage_get_attribute_scalar_value(
        stage,
        primPath,
        attributeName,
        timeSampled,
        timeCode,
        value,
        buffer.data(),
        buffer.size(),
        &required,
        error);
    if (status != OPENUSD_STATUS_OK)
    {
        return false;
    }
    *text = buffer.data();
    return true;
}

template <typename TValue>
bool ReadArray(
    const openusd_stage* stage,
    const char* primPath,
    const char* attributeName,
    int32_t timeSampled,
    double timeCode,
    ArrayGetter<TValue> getter,
    openusd_error_buffer* error,
    std::vector<TValue>* values)
{
    size_t required = 0;
    openusd_status status = getter(
        stage,
        primPath,
        attributeName,
        timeSampled,
        timeCode,
        nullptr,
        0,
        &required,
        error);
    if (status != OPENUSD_STATUS_OK)
    {
        return false;
    }
    if (required == 0)
    {
        values->clear();
        return true;
    }

    values->resize(required);
    status = getter(
        stage,
        primPath,
        attributeName,
        timeSampled,
        timeCode,
        values->data(),
        values->size(),
        &required,
        error);
    return status == OPENUSD_STATUS_OK && required == values->size();
}

template <typename TValue>
bool ReadGeomArray(
    const openusd_stage* stage,
    const char* primPath,
    GeomArrayGetter<TValue> getter,
    openusd_error_buffer* error,
    std::vector<TValue>* values)
{
    size_t required = 0;
    openusd_status status = getter(stage, primPath, nullptr, 0, &required, error);
    if (status != OPENUSD_STATUS_OK)
    {
        return false;
    }
    if (required == 0)
    {
        values->clear();
        return true;
    }
    values->resize(required);
    status = getter(
        stage, primPath, values->data(), values->size(), &required, error);
    return status == OPENUSD_STATUS_OK && required == values->size();
}

template <typename TValue>
bool ReadGeomArray(
    const openusd_stage* stage,
    const char* primPath,
    int32_t timeSampled,
    double timeCode,
    GeomTimedArrayGetter<TValue> getter,
    openusd_error_buffer* error,
    std::vector<TValue>* values)
{
    size_t required = 0;
    openusd_status status = getter(
        stage, primPath, timeSampled, timeCode, nullptr, 0, &required, error);
    if (status != OPENUSD_STATUS_OK)
    {
        return false;
    }
    if (required == 0)
    {
        values->clear();
        return true;
    }
    values->resize(required);
    status = getter(
        stage,
        primPath,
        timeSampled,
        timeCode,
        values->data(),
        values->size(),
        &required,
        error);
    return status == OPENUSD_STATUS_OK && required == values->size();
}

openusd_bounds3d MakeBoundsOutput()
{
    openusd_bounds3d bounds{};
    bounds.struct_size = static_cast<uint32_t>(sizeof(openusd_bounds3d));
    bounds.version = OPENUSD_BOUNDS3D_VERSION;
    return bounds;
}

openusd_bounds3d MakeBoundsSentinel()
{
    openusd_bounds3d bounds = MakeBoundsOutput();
    bounds.is_valid = 17;
    bounds.is_empty = 0;
    std::fill(std::begin(bounds.minimum), std::end(bounds.minimum), 19.0);
    std::fill(std::begin(bounds.maximum), std::end(bounds.maximum), 23.0);
    return bounds;
}

bool IsCanonicalBoundsFailure(const openusd_bounds3d& bounds)
{
    return bounds.struct_size == sizeof(openusd_bounds3d) &&
        bounds.version == OPENUSD_BOUNDS3D_VERSION &&
        bounds.is_valid == 0 && bounds.is_empty == 1 &&
        std::all_of(
            std::begin(bounds.minimum),
            std::end(bounds.minimum),
            [](double value) { return value == 0.0; }) &&
        std::all_of(
            std::begin(bounds.maximum),
            std::end(bounds.maximum),
            [](double value) { return value == 0.0; });
}

TfTokenVector GetReferencePurposes(uint32_t purposeMask)
{
    TfTokenVector purposes;
    if ((purposeMask & OPENUSD_GEOM_PURPOSE_MASK_DEFAULT) != 0)
    {
        purposes.push_back(UsdGeomTokens->default_);
    }
    if ((purposeMask & OPENUSD_GEOM_PURPOSE_MASK_PROXY) != 0)
    {
        purposes.push_back(UsdGeomTokens->proxy);
    }
    if ((purposeMask & OPENUSD_GEOM_PURPOSE_MASK_RENDER) != 0)
    {
        purposes.push_back(UsdGeomTokens->render);
    }
    if ((purposeMask & OPENUSD_GEOM_PURPOSE_MASK_GUIDE) != 0)
    {
        purposes.push_back(UsdGeomTokens->guide);
    }
    return purposes;
}

bool ComputeReferenceBounds(
    const UsdStageRefPtr& stage,
    const char* primPath,
    uint32_t purposeMask,
    int32_t timeSampled,
    double timeCode,
    openusd_bounds3d* bounds)
{
    *bounds = MakeBoundsOutput();
    bounds->is_valid = 1;
    bounds->is_empty = 1;
    if (!stage || purposeMask == 0)
    {
        return stage != nullptr;
    }

    const bool stageBounds = primPath == nullptr || primPath[0] == '\0';
    const UsdPrim prim = stageBounds
        ? stage->GetPseudoRoot()
        : stage->GetPrimAtPath(SdfPath(primPath));
    if (!prim || !prim.IsActive())
    {
        return true;
    }

    TfErrorMark mark;
    GfRange3d range;
    {
        UsdGeomBBoxCache cache(
            timeSampled != 0 ? UsdTimeCode(timeCode) : UsdTimeCode::Default(),
            GetReferencePurposes(purposeMask),
            true);
        range = cache.ComputeWorldBound(prim).ComputeAlignedRange();
    }
    if (!mark.IsClean())
    {
        mark.Clear();
        return false;
    }
    if (range.IsEmpty())
    {
        return true;
    }

    const GfVec3d minimum = range.GetMin();
    const GfVec3d maximum = range.GetMax();
    for (size_t index = 0; index < 3; ++index)
    {
        if (!std::isfinite(minimum[index]) || !std::isfinite(maximum[index]) ||
            minimum[index] > maximum[index] ||
            !std::isfinite(maximum[index] - minimum[index]))
        {
            return false;
        }
        bounds->minimum[index] = minimum[index];
        bounds->maximum[index] = maximum[index];
    }
    bounds->is_empty = 0;
    return true;
}

bool BoundsEqual(const openusd_bounds3d& left, const openusd_bounds3d& right)
{
    return left.struct_size == right.struct_size &&
        left.version == right.version &&
        left.is_valid == right.is_valid &&
        left.is_empty == right.is_empty &&
        std::equal(
            std::begin(left.minimum),
            std::end(left.minimum),
            std::begin(right.minimum)) &&
        std::equal(
            std::begin(left.maximum),
            std::end(left.maximum),
            std::begin(right.maximum));
}

bool VerifyWorldBounds(
    const std::filesystem::path& directory,
    openusd_error_buffer* error)
{
    const std::filesystem::path emptyPath = directory / "native-empty-bounds.usda";
    const std::filesystem::path modelPath = directory / "native-bounds-model.usda";
    const std::filesystem::path stagePath = directory / "native-world-bounds.usda";
    std::filesystem::remove(emptyPath);
    std::filesystem::remove(modelPath);
    std::filesystem::remove(stagePath);

    openusd_stage* emptyStage = nullptr;
    if (openusd_stage_create_new(emptyPath.string().c_str(), &emptyStage, error) !=
        OPENUSD_STATUS_OK)
    {
        return false;
    }
    openusd_bounds3d emptyBounds = MakeBoundsOutput();
    openusd_bounds3d emptyPathBounds = MakeBoundsOutput();
    const bool emptySucceeded =
        openusd_stage_get_world_bounds(
            emptyStage,
            nullptr,
            OPENUSD_GEOM_PURPOSE_MASK_ALL,
            0,
            0,
            &emptyBounds,
            error) == OPENUSD_STATUS_OK &&
        openusd_stage_get_world_bounds(
            emptyStage,
            "",
            OPENUSD_GEOM_PURPOSE_MASK_ALL,
            0,
            0,
            &emptyPathBounds,
            error) == OPENUSD_STATUS_OK &&
        emptyBounds.is_valid == 1 && emptyBounds.is_empty == 1 &&
        BoundsEqual(emptyBounds, emptyPathBounds);
    openusd_stage_release(emptyStage);
    if (!emptySucceeded)
    {
        return false;
    }

    const openusd_extent3f modelExtent{
        {1.0F, 2.0F, 3.0F},
        {4.0F, 6.0F, 8.0F}};
    openusd_stage* modelStage = nullptr;
    if (openusd_stage_create_new(modelPath.string().c_str(), &modelStage, error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_define_xform(modelStage, "/Model", error) != OPENUSD_STATUS_OK ||
        openusd_geom_define_mesh(modelStage, "/Model/Mesh", error) != OPENUSD_STATUS_OK ||
        openusd_geom_mesh_set_extent(
            modelStage, "/Model/Mesh", &modelExtent, 0, 0, error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_save(modelStage, error) != OPENUSD_STATUS_OK)
    {
        openusd_stage_release(modelStage);
        return false;
    }
    openusd_stage_release(modelStage);

    const openusd_extent3f hierarchyExtent{
        {-2.0F, -1.0F, 0.0F},
        {2.0F, 1.0F, 0.0F}};
    const openusd_extent3f defaultExtent{
        {0.0F, 0.0F, 0.0F},
        {1.0F, 1.0F, 1.0F}};
    const openusd_extent3f proxyExtent{
        {10.0F, 10.0F, 10.0F},
        {11.0F, 11.0F, 11.0F}};
    const openusd_extent3f renderExtent{
        {20.0F, 20.0F, 20.0F},
        {21.0F, 21.0F, 21.0F}};
    const openusd_extent3f guideExtent{
        {30.0F, 30.0F, 30.0F},
        {31.0F, 31.0F, 31.0F}};
    const openusd_extent3f animatedDefaultExtent{
        {-1.0F, -1.0F, -1.0F},
        {1.0F, 1.0F, 1.0F}};
    const openusd_extent3f animatedSampleExtent{
        {-3.0F, -2.0F, -1.0F},
        {3.0F, 2.0F, 1.0F}};
    const openusd_matrix4d hierarchyTransform{{
        1.0, 0.0, 0.0, 0.0,
        0.0, 1.0, 0.0, 0.0,
        0.0, 0.0, 1.0, 0.0,
        5.0, 6.0, 7.0, 1.0}};

    openusd_stage* stage = nullptr;
    if (openusd_stage_create_new(stagePath.string().c_str(), &stage, error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_define_xform(stage, "/World", error) != OPENUSD_STATUS_OK ||
        openusd_geom_define_xform(stage, "/World/Hierarchy", error) != OPENUSD_STATUS_OK ||
        openusd_geom_define_mesh(stage, "/World/Hierarchy/Mesh", error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_xformable_set_local_transform(
            stage, "/World/Hierarchy", &hierarchyTransform, 0, 0, error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_mesh_set_extent(
            stage, "/World/Hierarchy/Mesh", &hierarchyExtent, 0, 0, error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_define_xform(stage, "/World/Purposes", error) != OPENUSD_STATUS_OK ||
        openusd_geom_define_mesh(stage, "/World/Purposes/Default", error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_define_mesh(stage, "/World/Purposes/Proxy", error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_define_mesh(stage, "/World/Purposes/Render", error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_define_mesh(stage, "/World/Purposes/Guide", error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_mesh_set_extent(
            stage, "/World/Purposes/Default", &defaultExtent, 0, 0, error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_mesh_set_extent(
            stage, "/World/Purposes/Proxy", &proxyExtent, 0, 0, error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_mesh_set_extent(
            stage, "/World/Purposes/Render", &renderExtent, 0, 0, error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_mesh_set_extent(
            stage, "/World/Purposes/Guide", &guideExtent, 0, 0, error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_imageable_set_purpose(
            stage, "/World/Purposes/Proxy", OPENUSD_GEOM_PURPOSE_PROXY, error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_imageable_set_purpose(
            stage, "/World/Purposes/Render", OPENUSD_GEOM_PURPOSE_RENDER, error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_imageable_set_purpose(
            stage, "/World/Purposes/Guide", OPENUSD_GEOM_PURPOSE_GUIDE, error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_define_mesh(stage, "/World/Animated", error) != OPENUSD_STATUS_OK ||
        openusd_geom_mesh_set_extent(
            stage, "/World/Animated", &animatedDefaultExtent, 0, 0, error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_mesh_set_extent(
            stage, "/World/Animated", &animatedSampleExtent, 1, 10.0, error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_define_mesh(stage, "/World/Inactive", error) != OPENUSD_STATUS_OK ||
        openusd_geom_mesh_set_extent(
            stage, "/World/Inactive", &defaultExtent, 0, 0, error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_prim_active(stage, "/World/Inactive", 0, error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_define_xform(stage, "/World/Instance", error) != OPENUSD_STATUS_OK ||
        openusd_stage_add_reference(
            stage,
            "/World/Instance",
            modelPath.string().c_str(),
            "/Model",
            error) != OPENUSD_STATUS_OK ||
        openusd_stage_set_instanceable(stage, "/World/Instance", 1, error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_define_xform(stage, "/World/Payload", error) != OPENUSD_STATUS_OK ||
        openusd_stage_add_payload(
            stage,
            "/World/Payload",
            modelPath.string().c_str(),
            "/Model",
            error) != OPENUSD_STATUS_OK ||
        openusd_stage_save(stage, error) != OPENUSD_STATUS_OK)
    {
        openusd_stage_release(stage);
        return false;
    }

    const UsdStageRefPtr referenceStage = UsdStage::Open(stagePath.string());
    if (!referenceStage)
    {
        openusd_stage_release(stage);
        return false;
    }
    const auto compareReference =
        [&](const char* primPath, uint32_t mask, int32_t sampled, double time)
        {
            openusd_bounds3d actual = MakeBoundsOutput();
            openusd_bounds3d expected{};
            return openusd_stage_get_world_bounds(
                       stage,
                       primPath,
                       mask,
                       sampled,
                       time,
                       &actual,
                       error) == OPENUSD_STATUS_OK &&
                ComputeReferenceBounds(
                    referenceStage,
                    primPath,
                    mask,
                    sampled,
                    time,
                    &expected) &&
                BoundsEqual(actual, expected);
        };

    openusd_bounds3d hierarchyBounds = MakeBoundsOutput();
    openusd_bounds3d defaultPurposeBounds = MakeBoundsOutput();
    openusd_bounds3d proxyPurposeBounds = MakeBoundsOutput();
    openusd_bounds3d renderPurposeBounds = MakeBoundsOutput();
    openusd_bounds3d guidePurposeBounds = MakeBoundsOutput();
    openusd_bounds3d allPurposeBounds = MakeBoundsOutput();
    openusd_bounds3d noPurposeBounds = MakeBoundsOutput();
    openusd_bounds3d animatedDefaultBounds = MakeBoundsOutput();
    openusd_bounds3d animatedSampleBounds = MakeBoundsOutput();
    openusd_bounds3d inactiveBounds = MakeBoundsOutput();
    openusd_bounds3d missingBounds = MakeBoundsOutput();
    if (!compareReference(nullptr, OPENUSD_GEOM_PURPOSE_MASK_ALL, 0, 0) ||
        !compareReference("/World/Hierarchy", OPENUSD_GEOM_PURPOSE_MASK_ALL, 0, 0) ||
        openusd_stage_get_world_bounds(
            stage,
            "/World/Hierarchy",
            OPENUSD_GEOM_PURPOSE_MASK_ALL,
            0,
            0,
            &hierarchyBounds,
            error) != OPENUSD_STATUS_OK ||
        hierarchyBounds.minimum[0] != 3.0 ||
        hierarchyBounds.minimum[1] != 5.0 ||
        hierarchyBounds.minimum[2] != 7.0 ||
        hierarchyBounds.maximum[0] != 7.0 ||
        hierarchyBounds.maximum[1] != 7.0 ||
        hierarchyBounds.maximum[2] != 7.0 ||
        !compareReference(
            "/World/Purposes", OPENUSD_GEOM_PURPOSE_MASK_DEFAULT, 0, 0) ||
        !compareReference(
            "/World/Purposes", OPENUSD_GEOM_PURPOSE_MASK_PROXY, 0, 0) ||
        !compareReference(
            "/World/Purposes", OPENUSD_GEOM_PURPOSE_MASK_RENDER, 0, 0) ||
        !compareReference(
            "/World/Purposes", OPENUSD_GEOM_PURPOSE_MASK_GUIDE, 0, 0) ||
        !compareReference(
            "/World/Purposes", OPENUSD_GEOM_PURPOSE_MASK_ALL, 0, 0) ||
        openusd_stage_get_world_bounds(
            stage,
            "/World/Purposes",
            OPENUSD_GEOM_PURPOSE_MASK_DEFAULT,
            0,
            0,
            &defaultPurposeBounds,
            error) != OPENUSD_STATUS_OK ||
        openusd_stage_get_world_bounds(
            stage,
            "/World/Purposes",
            OPENUSD_GEOM_PURPOSE_MASK_PROXY,
            0,
            0,
            &proxyPurposeBounds,
            error) != OPENUSD_STATUS_OK ||
        openusd_stage_get_world_bounds(
            stage,
            "/World/Purposes",
            OPENUSD_GEOM_PURPOSE_MASK_RENDER,
            0,
            0,
            &renderPurposeBounds,
            error) != OPENUSD_STATUS_OK ||
        openusd_stage_get_world_bounds(
            stage,
            "/World/Purposes",
            OPENUSD_GEOM_PURPOSE_MASK_GUIDE,
            0,
            0,
            &guidePurposeBounds,
            error) != OPENUSD_STATUS_OK ||
        openusd_stage_get_world_bounds(
            stage,
            "/World/Purposes",
            OPENUSD_GEOM_PURPOSE_MASK_ALL,
            0,
            0,
            &allPurposeBounds,
            error) != OPENUSD_STATUS_OK ||
        openusd_stage_get_world_bounds(
            stage,
            "/World/Purposes",
            0,
            0,
            0,
            &noPurposeBounds,
            error) != OPENUSD_STATUS_OK ||
        defaultPurposeBounds.minimum[0] != 0.0 ||
        defaultPurposeBounds.maximum[0] != 1.0 ||
        proxyPurposeBounds.minimum[0] != 10.0 ||
        proxyPurposeBounds.maximum[0] != 11.0 ||
        renderPurposeBounds.minimum[0] != 20.0 ||
        renderPurposeBounds.maximum[0] != 21.0 ||
        guidePurposeBounds.minimum[0] != 30.0 ||
        guidePurposeBounds.maximum[0] != 31.0 ||
        allPurposeBounds.minimum[0] != 0.0 ||
        allPurposeBounds.maximum[0] != 31.0 ||
        noPurposeBounds.is_valid != 1 || noPurposeBounds.is_empty != 1 ||
        !compareReference("/World/Animated", OPENUSD_GEOM_PURPOSE_MASK_ALL, 0, 0) ||
        !compareReference("/World/Animated", OPENUSD_GEOM_PURPOSE_MASK_ALL, 1, 10.0) ||
        openusd_stage_get_world_bounds(
            stage,
            "/World/Animated",
            OPENUSD_GEOM_PURPOSE_MASK_ALL,
            0,
            0,
            &animatedDefaultBounds,
            error) != OPENUSD_STATUS_OK ||
        openusd_stage_get_world_bounds(
            stage,
            "/World/Animated",
            OPENUSD_GEOM_PURPOSE_MASK_ALL,
            1,
            10.0,
            &animatedSampleBounds,
            error) != OPENUSD_STATUS_OK ||
        animatedDefaultBounds.minimum[0] != -1.0 ||
        animatedDefaultBounds.maximum[0] != 1.0 ||
        animatedSampleBounds.minimum[0] != -3.0 ||
        animatedSampleBounds.maximum[0] != 3.0 ||
        openusd_stage_get_world_bounds(
            stage,
            "/World/Inactive",
            OPENUSD_GEOM_PURPOSE_MASK_ALL,
            0,
            0,
            &inactiveBounds,
            error) != OPENUSD_STATUS_OK ||
        inactiveBounds.is_valid != 1 || inactiveBounds.is_empty != 1 ||
        openusd_stage_get_world_bounds(
            stage,
            "/World/Missing",
            OPENUSD_GEOM_PURPOSE_MASK_ALL,
            0,
            0,
            &missingBounds,
            error) != OPENUSD_STATUS_OK ||
        missingBounds.is_valid != 1 || missingBounds.is_empty != 1 ||
        !compareReference("/World/Instance", OPENUSD_GEOM_PURPOSE_MASK_ALL, 0, 0) ||
        !compareReference("/World/Payload", OPENUSD_GEOM_PURPOSE_MASK_ALL, 0, 0))
    {
        openusd_stage_release(stage);
        return false;
    }

    const openusd_extent3f overflowExtent{
        {-1.0F, 0.0F, 0.0F},
        {1.0F, 0.0F, 0.0F}};
    const double maximumDouble = std::numeric_limits<double>::max();
    const openusd_matrix4d overflowTransform{{
        maximumDouble, 0.0, 0.0, 0.0,
        0.0, 1.0, 0.0, 0.0,
        0.0, 0.0, 1.0, 0.0,
        0.0, 0.0, 0.0, 1.0}};
    openusd_bounds3d overflowBounds = MakeBoundsSentinel();
    if (openusd_geom_define_mesh(stage, "/World/Overflow", error) != OPENUSD_STATUS_OK ||
        openusd_geom_mesh_set_extent(
            stage, "/World/Overflow", &overflowExtent, 0, 0, error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_xformable_set_local_transform(
            stage, "/World/Overflow", &overflowTransform, 0, 0, error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_get_world_bounds(
            stage,
            "/World/Overflow",
            OPENUSD_GEOM_PURPOSE_MASK_ALL,
            0,
            0,
            &overflowBounds,
            error) != OPENUSD_STATUS_NATIVE_ERROR ||
        !IsCanonicalBoundsFailure(overflowBounds))
    {
        openusd_stage_release(stage);
        return false;
    }

    std::string prototypePath;
    if (!ReadPrimString(
            stage,
            "/World/Instance",
            openusd_stage_get_prim_prototype_path,
            error,
            &prototypePath) ||
        prototypePath.empty() ||
        !compareReference(
            prototypePath.c_str(),
            OPENUSD_GEOM_PURPOSE_MASK_ALL,
            0,
            0))
    {
        openusd_stage_release(stage);
        return false;
    }

    if (openusd_stage_unload_prim(stage, "/World/Payload", error) != OPENUSD_STATUS_OK)
    {
        openusd_stage_release(stage);
        return false;
    }
    const UsdPrim referencePayload =
        referenceStage->GetPrimAtPath(SdfPath("/World/Payload"));
    referencePayload.Unload();
    openusd_bounds3d unloadedBounds = MakeBoundsOutput();
    openusd_bounds3d unloadedReference{};
    if (openusd_stage_get_world_bounds(
            stage,
            "/World/Payload",
            OPENUSD_GEOM_PURPOSE_MASK_ALL,
            0,
            0,
            &unloadedBounds,
            error) != OPENUSD_STATUS_OK ||
        !ComputeReferenceBounds(
            referenceStage,
            "/World/Payload",
            OPENUSD_GEOM_PURPOSE_MASK_ALL,
            0,
            0,
            &unloadedReference) ||
        !BoundsEqual(unloadedBounds, unloadedReference) ||
        unloadedBounds.is_empty != 1)
    {
        openusd_stage_release(stage);
        return false;
    }

    const auto rejectsInvalid =
        [&](const openusd_stage* inputStage,
            const char* primPath,
            uint32_t mask,
            int32_t sampled,
            double time)
        {
            openusd_bounds3d bounds = MakeBoundsSentinel();
            return openusd_stage_get_world_bounds(
                       inputStage,
                       primPath,
                       mask,
                       sampled,
                       time,
                       &bounds,
                       error) == OPENUSD_STATUS_INVALID_ARGUMENT &&
                IsCanonicalBoundsFailure(bounds);
        };
    if (!rejectsInvalid(
            nullptr, nullptr, OPENUSD_GEOM_PURPOSE_MASK_ALL, 0, 0) ||
        !rejectsInvalid(
            stage, "relative", OPENUSD_GEOM_PURPOSE_MASK_ALL, 0, 0) ||
        !rejectsInvalid(
            stage, "/", OPENUSD_GEOM_PURPOSE_MASK_ALL, 0, 0) ||
        !rejectsInvalid(stage, nullptr, UINT32_C(1) << 31, 0, 0) ||
        !rejectsInvalid(
            stage, nullptr, OPENUSD_GEOM_PURPOSE_MASK_ALL, 2, 0) ||
        !rejectsInvalid(
            stage,
            nullptr,
            OPENUSD_GEOM_PURPOSE_MASK_ALL,
            1,
            std::numeric_limits<double>::quiet_NaN()) ||
        !rejectsInvalid(
            stage,
            nullptr,
            OPENUSD_GEOM_PURPOSE_MASK_ALL,
            1,
            std::numeric_limits<double>::infinity()))
    {
        openusd_stage_release(stage);
        return false;
    }

    openusd_bounds3d wrongVersion = MakeBoundsSentinel();
    wrongVersion.version = OPENUSD_BOUNDS3D_VERSION + 1;
    if (openusd_stage_get_world_bounds(
            stage,
            nullptr,
            OPENUSD_GEOM_PURPOSE_MASK_ALL,
            0,
            0,
            &wrongVersion,
            error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        !IsCanonicalBoundsFailure(wrongVersion))
    {
        openusd_stage_release(stage);
        return false;
    }

    openusd_bounds3d undersized = MakeBoundsSentinel();
    undersized.struct_size = sizeof(uint32_t) * 2;
    if (openusd_stage_get_world_bounds(
            stage,
            nullptr,
            OPENUSD_GEOM_PURPOSE_MASK_ALL,
            0,
            0,
            &undersized,
            error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        undersized.struct_size != sizeof(uint32_t) * 2 ||
        undersized.version != OPENUSD_BOUNDS3D_VERSION ||
        undersized.is_valid != 17 || undersized.is_empty != 0)
    {
        openusd_stage_release(stage);
        return false;
    }

    alignas(openusd_bounds3d)
        std::array<unsigned char, sizeof(openusd_bounds3d) + 1> misalignedBytes{};
    const openusd_bounds3d misalignedSentinel = MakeBoundsSentinel();
    std::memcpy(misalignedBytes.data() + 1, &misalignedSentinel, sizeof(misalignedSentinel));
    auto* misalignedBounds =
        reinterpret_cast<openusd_bounds3d*>(misalignedBytes.data() + 1);
    const openusd_status misalignedStatus = openusd_stage_get_world_bounds(
        stage,
        nullptr,
        OPENUSD_GEOM_PURPOSE_MASK_ALL,
        0,
        0,
        misalignedBounds,
        error);
    openusd_bounds3d resetMisaligned{};
    std::memcpy(&resetMisaligned, misalignedBytes.data() + 1, sizeof(resetMisaligned));
    if (misalignedStatus != OPENUSD_STATUS_INVALID_ARGUMENT ||
        !IsCanonicalBoundsFailure(resetMisaligned) ||
        openusd_stage_get_world_bounds(
            stage,
            nullptr,
            OPENUSD_GEOM_PURPOSE_MASK_ALL,
            0,
            0,
            nullptr,
            error) != OPENUSD_STATUS_INVALID_ARGUMENT)
    {
        openusd_stage_release(stage);
        return false;
    }

    openusd_stage_release(stage);
    return true;
}

bool VerifyWorldTransforms(
    const std::filesystem::path& directory,
    openusd_error_buffer* error)
{
    const std::filesystem::path stagePath =
        directory / "native-world-transforms.usda";
    std::filesystem::remove(stagePath);

    const UsdStageRefPtr referenceStage = UsdStage::CreateNew(stagePath.string());
    if (!referenceStage)
    {
        return false;
    }

    const UsdGeomXform world =
        UsdGeomXform::Define(referenceStage, SdfPath("/World"));
    const UsdGeomXform hierarchy =
        UsdGeomXform::Define(referenceStage, SdfPath("/World/Hierarchy"));
    const UsdGeomXform child =
        UsdGeomXform::Define(referenceStage, SdfPath("/World/Hierarchy/Child"));
    const UsdGeomXform reset =
        UsdGeomXform::Define(referenceStage, SdfPath("/World/Hierarchy/Reset"));
    const UsdGeomXform inactive =
        UsdGeomXform::Define(referenceStage, SdfPath("/World/Inactive"));
    const UsdGeomXform nonFinite =
        UsdGeomXform::Define(referenceStage, SdfPath("/World/NonFinite"));
    const UsdGeomXform model =
        UsdGeomXform::Define(referenceStage, SdfPath("/Model"));
    const UsdGeomXform modelChild =
        UsdGeomXform::Define(referenceStage, SdfPath("/Model/Child"));
    const UsdPrim scope =
        referenceStage->DefinePrim(SdfPath("/World/Scope"), TfToken("Scope"));
    if (!world || !hierarchy || !child || !reset || !inactive || !nonFinite ||
        !model || !modelChild || !scope)
    {
        return false;
    }

    const UsdGeomXformable worldXform(world.GetPrim());
    const UsdGeomXformable hierarchyXform(hierarchy.GetPrim());
    const UsdGeomXformable childXform(child.GetPrim());
    const UsdGeomXformable resetXform(reset.GetPrim());
    const UsdGeomXformable nonFiniteXform(nonFinite.GetPrim());
    const UsdGeomXformable modelXform(model.GetPrim());
    const UsdGeomXformable modelChildXform(modelChild.GetPrim());
    const UsdGeomXformOp worldTranslate = worldXform.AddTranslateOp();
    const UsdGeomXformOp hierarchyTranslate = hierarchyXform.AddTranslateOp();
    const UsdGeomXformOp hierarchyRotate = hierarchyXform.AddRotateXYZOp();
    const UsdGeomXformOp hierarchyScale = hierarchyXform.AddScaleOp();
    const UsdGeomXformOp childTranslate = childXform.AddTranslateOp();
    const UsdGeomXformOp childRotate = childXform.AddRotateZOp();
    const UsdGeomXformOp resetTranslate = resetXform.AddTranslateOp();
    const UsdGeomXformOp nonFiniteTransform = nonFiniteXform.AddTransformOp();
    const UsdGeomXformOp modelTranslate = modelXform.AddTranslateOp();
    const UsdGeomXformOp modelChildTranslate = modelChildXform.AddTranslateOp();
    GfMatrix4d nonFiniteDefault(1.0);
    nonFiniteDefault[0][2] = std::numeric_limits<double>::quiet_NaN();
    GfMatrix4d nonFiniteSample(1.0);
    nonFiniteSample[3][1] = std::numeric_limits<double>::infinity();
    if (!worldTranslate || !hierarchyTranslate || !hierarchyRotate ||
        !hierarchyScale || !childTranslate || !childRotate ||
        !resetTranslate || !nonFiniteTransform ||
        !modelTranslate || !modelChildTranslate ||
        !worldTranslate.Set(GfVec3d(10.0, 0.0, 0.0)) ||
        !worldTranslate.Set(GfVec3d(20.0, 0.0, 0.0), UsdTimeCode(10.0)) ||
        !hierarchyTranslate.Set(GfVec3d(1.0, 2.0, 3.0)) ||
        !hierarchyTranslate.Set(GfVec3d(4.0, 5.0, 6.0), UsdTimeCode(10.0)) ||
        !hierarchyRotate.Set(GfVec3f(15.0F, 25.0F, 35.0F)) ||
        !hierarchyRotate.Set(
            GfVec3f(45.0F, 55.0F, 65.0F), UsdTimeCode(10.0)) ||
        !hierarchyScale.Set(GfVec3f(2.0F, 3.0F, 4.0F)) ||
        !hierarchyScale.Set(
            GfVec3f(1.5F, 2.5F, 3.5F), UsdTimeCode(10.0)) ||
        !childTranslate.Set(GfVec3d(-2.0, 4.0, 8.0)) ||
        !childTranslate.Set(GfVec3d(3.0, 6.0, 9.0), UsdTimeCode(10.0)) ||
        !childRotate.Set(12.5F) ||
        !childRotate.Set(42.5F, UsdTimeCode(10.0)) ||
        !resetTranslate.Set(GfVec3d(-7.0, 11.0, 13.0)) ||
        !resetXform.SetResetXformStack(true) ||
        !nonFiniteTransform.Set(nonFiniteDefault) ||
        !nonFiniteTransform.Set(nonFiniteSample, UsdTimeCode(10.0)) ||
        !modelTranslate.Set(GfVec3d(100.0, 200.0, 300.0)) ||
        !modelChildTranslate.Set(GfVec3d(5.0, 6.0, 7.0)))
    {
        return false;
    }

    UsdPrim instance = referenceStage->OverridePrim(SdfPath("/World/Instance"));
    if (!instance ||
        !instance.GetReferences().AddInternalReference(SdfPath("/Model")) ||
        !instance.SetInstanceable(true) ||
        !inactive.GetPrim().SetActive(false) ||
        !referenceStage->GetRootLayer()->Save())
    {
        return false;
    }

    const UsdPrim instanceProxy =
        referenceStage->GetPrimAtPath(SdfPath("/World/Instance/Child"));
    const UsdPrim prototype = instance.GetPrototype();
    const UsdPrim prototypeChild = instanceProxy.GetPrimInPrototype();
    if (!instanceProxy || !instanceProxy.IsInstanceProxy() ||
        !prototype || !prototype.IsPrototype() ||
        !prototypeChild || !prototypeChild.IsInPrototype() ||
        !UsdGeomXformable(prototypeChild))
    {
        return false;
    }

    openusd_stage* stage = nullptr;
    if (openusd_stage_open(stagePath.string().c_str(), &stage, error) !=
        OPENUSD_STATUS_OK)
    {
        return false;
    }

    const auto isZeroMatrix = [](const openusd_matrix4d& value)
    {
        return std::all_of(
            std::begin(value.values),
            std::end(value.values),
            [](double item) { return item == 0.0; });
    };
    const auto matchesReference =
        [&](const UsdPrim& prim, int32_t sampled, double timeCode)
        {
            openusd_matrix4d actual{};
            std::fill(std::begin(actual.values), std::end(actual.values), -991.0);
            const openusd_status status =
                openusd_geom_xformable_get_world_transform(
                    stage,
                    prim.GetPath().GetText(),
                    sampled,
                    timeCode,
                    &actual,
                    error);
            TfErrorMark mark;
            UsdGeomXformCache cache(
                sampled != 0
                    ? UsdTimeCode(timeCode)
                    : UsdTimeCode::Default());
            const GfMatrix4d expected = cache.GetLocalToWorldTransform(prim);
            if (!mark.IsClean())
            {
                mark.Clear();
                return false;
            }
            for (int row = 0; row < 4; ++row)
            {
                for (int column = 0; column < 4; ++column)
                {
                    if (actual.values[(row * 4) + column] !=
                        expected[row][column])
                    {
                        return false;
                    }
                }
            }
            return status == OPENUSD_STATUS_OK;
        };

    const UsdPrim childPrim = child.GetPrim();
    const UsdPrim resetPrim = reset.GetPrim();
    if (!matchesReference(childPrim, 0, 0.0) ||
        !matchesReference(childPrim, 1, 10.0) ||
        !matchesReference(resetPrim, 0, 0.0) ||
        !matchesReference(instance, 0, 0.0) ||
        !matchesReference(instanceProxy, 0, 0.0) ||
        !matchesReference(prototypeChild, 0, 0.0))
    {
        openusd_stage_release(stage);
        return false;
    }

    openusd_matrix4d resetWorld{};
    if (openusd_geom_xformable_get_world_transform(
            stage,
            resetPrim.GetPath().GetText(),
            0,
            0.0,
            &resetWorld,
            error) != OPENUSD_STATUS_OK ||
        resetWorld.values[12] != -7.0 ||
        resetWorld.values[13] != 11.0 ||
        resetWorld.values[14] != 13.0)
    {
        openusd_stage_release(stage);
        return false;
    }

    const auto rejectsWithZero =
        [&](const openusd_stage* inputStage,
            const char* primPath,
            int32_t sampled,
            double timeCode,
            openusd_status expected)
        {
            openusd_matrix4d value{};
            std::fill(std::begin(value.values), std::end(value.values), 997.0);
            return openusd_geom_xformable_get_world_transform(
                       inputStage,
                       primPath,
                       sampled,
                       timeCode,
                       &value,
                       error) == expected &&
                isZeroMatrix(value);
        };
    if (!rejectsWithZero(
            nullptr, "/World/Hierarchy/Child", 0, 0.0,
            OPENUSD_STATUS_INVALID_ARGUMENT) ||
        !rejectsWithZero(
            stage, "relative", 0, 0.0, OPENUSD_STATUS_INVALID_ARGUMENT) ||
        !rejectsWithZero(
            stage, "/", 0, 0.0, OPENUSD_STATUS_INVALID_ARGUMENT) ||
        !rejectsWithZero(
            stage, "/World/Hierarchy/Child", 2, 0.0,
            OPENUSD_STATUS_INVALID_ARGUMENT) ||
        !rejectsWithZero(
            stage,
            "/World/Hierarchy/Child",
            1,
            std::numeric_limits<double>::quiet_NaN(),
            OPENUSD_STATUS_INVALID_ARGUMENT) ||
        !rejectsWithZero(
            stage,
            "/World/Hierarchy/Child",
            1,
            std::numeric_limits<double>::infinity(),
            OPENUSD_STATUS_INVALID_ARGUMENT) ||
        !rejectsWithZero(
            stage, "/World/Missing", 0, 0.0, OPENUSD_STATUS_NOT_FOUND) ||
        !rejectsWithZero(
            stage, "/World/Scope", 0, 0.0, OPENUSD_STATUS_INVALID_ARGUMENT) ||
        !rejectsWithZero(
            stage, "/World/Inactive", 0, 0.0, OPENUSD_STATUS_NOT_FOUND) ||
        !rejectsWithZero(
            stage, "/World/NonFinite", 0, 0.0, OPENUSD_STATUS_NATIVE_ERROR) ||
        !rejectsWithZero(
            stage, "/World/NonFinite", 1, 10.0, OPENUSD_STATUS_NATIVE_ERROR) ||
        openusd_geom_xformable_get_world_transform(
            stage,
            "/World/Hierarchy/Child",
            0,
            0.0,
            nullptr,
            error) != OPENUSD_STATUS_INVALID_ARGUMENT)
    {
        openusd_stage_release(stage);
        return false;
    }

    alignas(openusd_matrix4d)
        std::array<unsigned char, sizeof(openusd_matrix4d) + 1> misalignedBytes{};
    std::fill(misalignedBytes.begin(), misalignedBytes.end(), 0xA5);
    auto* misaligned =
        reinterpret_cast<openusd_matrix4d*>(misalignedBytes.data() + 1);
    const openusd_status misalignedStatus =
        openusd_geom_xformable_get_world_transform(
            stage,
            "/World/Hierarchy/Child",
            0,
            0.0,
            misaligned,
            error);
    if (misalignedStatus != OPENUSD_STATUS_INVALID_ARGUMENT ||
        !std::all_of(
            misalignedBytes.begin() + 1,
            misalignedBytes.end(),
            [](unsigned char value) { return value == 0; }))
    {
        openusd_stage_release(stage);
        return false;
    }

    SetWorldTransformFailpoint("after-compute");
    openusd_matrix4d diagnosticValue{};
    std::fill(
        std::begin(diagnosticValue.values),
        std::end(diagnosticValue.values),
        1009.0);
    const openusd_status diagnosticStatus =
        openusd_geom_xformable_get_world_transform(
            stage,
            "/World/Hierarchy/Child",
            0,
            0.0,
            &diagnosticValue,
            error);
    SetWorldTransformFailpoint(nullptr);
    if (diagnosticStatus != OPENUSD_STATUS_NATIVE_ERROR ||
        !isZeroMatrix(diagnosticValue) ||
        error->required == 0)
    {
        openusd_stage_release(stage);
        return false;
    }

    openusd_stage_release(stage);
    std::filesystem::remove(stagePath);
    return true;
}

bool VerifyCameraStates(
    const std::filesystem::path& directory,
    openusd_error_buffer* error)
{
    const std::filesystem::path stagePath =
        directory / "native-camera-states.usda";
    std::filesystem::remove(stagePath);

    const UsdStageRefPtr referenceStage = UsdStage::CreateNew(stagePath.string());
    if (!referenceStage)
    {
        return false;
    }

    const UsdGeomCamera animated =
        UsdGeomCamera::Define(referenceStage, SdfPath("/World/Animated"));
    const UsdGeomCamera orthographic =
        UsdGeomCamera::Define(referenceStage, SdfPath("/World/Orthographic"));
    const UsdGeomCamera perspectiveZero =
        UsdGeomCamera::Define(referenceStage, SdfPath("/World/PerspectiveZero"));
    const UsdGeomCamera negativeFocal =
        UsdGeomCamera::Define(referenceStage, SdfPath("/World/NegativeFocal"));
    const UsdGeomCamera inactive =
        UsdGeomCamera::Define(referenceStage, SdfPath("/World/Inactive"));
    const UsdGeomCamera invalidWindow =
        UsdGeomCamera::Define(referenceStage, SdfPath("/World/InvalidWindow"));
    const UsdGeomCamera invalidClipping =
        UsdGeomCamera::Define(referenceStage, SdfPath("/World/InvalidClipping"));
    const UsdGeomCamera nonFinite =
        UsdGeomCamera::Define(referenceStage, SdfPath("/World/NonFinite"));
    const UsdGeomCamera malformed =
        UsdGeomCamera::Define(referenceStage, SdfPath("/World/Malformed"));
    const UsdGeomXform wrongSchema =
        UsdGeomXform::Define(referenceStage, SdfPath("/World/WrongSchema"));
    if (!animated || !orthographic || !perspectiveZero || !negativeFocal ||
        !inactive || !invalidWindow || !invalidClipping || !nonFinite ||
        !malformed || !wrongSchema)
    {
        return false;
    }

    const UsdTimeCode sampleTime(10.0);
    if (!animated.CreateProjectionAttr().Set(UsdGeomTokens->perspective) ||
        !animated.CreateProjectionAttr().Set(
            UsdGeomTokens->orthographic, sampleTime) ||
        !animated.CreateHorizontalApertureAttr().Set(36.0F) ||
        !animated.CreateHorizontalApertureAttr().Set(48.0F, sampleTime) ||
        !animated.CreateVerticalApertureAttr().Set(24.0F) ||
        !animated.CreateVerticalApertureAttr().Set(20.0F, sampleTime) ||
        !animated.CreateHorizontalApertureOffsetAttr().Set(3.0F) ||
        !animated.CreateHorizontalApertureOffsetAttr().Set(-4.0F, sampleTime) ||
        !animated.CreateVerticalApertureOffsetAttr().Set(-2.0F) ||
        !animated.CreateVerticalApertureOffsetAttr().Set(5.0F, sampleTime) ||
        !animated.CreateFocalLengthAttr().Set(60.0F) ||
        !animated.CreateFocalLengthAttr().Set(0.0F, sampleTime) ||
        !animated.CreateClippingRangeAttr().Set(GfVec2f(0.5F, 500.0F)) ||
        !animated.CreateClippingRangeAttr().Set(
            GfVec2f(2.0F, 250.0F), sampleTime) ||
        !animated.CreateFocusDistanceAttr().Set(12.0F) ||
        !animated.CreateFocusDistanceAttr().Set(24.0F, sampleTime) ||
        !animated.CreateFStopAttr().Set(2.8F) ||
        !animated.CreateFStopAttr().Set(5.6F, sampleTime) ||
        !orthographic.CreateProjectionAttr().Set(UsdGeomTokens->orthographic) ||
        !orthographic.CreateHorizontalApertureAttr().Set(30.0F) ||
        !orthographic.CreateVerticalApertureAttr().Set(20.0F) ||
        !orthographic.CreateHorizontalApertureOffsetAttr().Set(7.0F) ||
        !orthographic.CreateVerticalApertureOffsetAttr().Set(-3.0F) ||
        !orthographic.CreateClippingRangeAttr().Set(GfVec2f(-10.0F, 100.0F)) ||
        !orthographic.CreateFocalLengthAttr().Set(0.0F) ||
        !perspectiveZero.CreateProjectionAttr().Set(UsdGeomTokens->perspective) ||
        !perspectiveZero.CreateFocalLengthAttr().Set(0.0F) ||
        !negativeFocal.CreateProjectionAttr().Set(UsdGeomTokens->orthographic) ||
        !negativeFocal.CreateFocalLengthAttr().Set(-1.0F) ||
        !invalidWindow.CreateHorizontalApertureAttr().Set(0.0F) ||
        !invalidClipping.CreateClippingRangeAttr().Set(GfVec2f(10.0F, 1.0F)) ||
        !nonFinite.CreateVerticalApertureOffsetAttr().Set(
            std::numeric_limits<float>::infinity()) ||
        !malformed.CreateProjectionAttr().Set(TfToken("fisheye")) ||
        !inactive.GetPrim().SetActive(false) ||
        !referenceStage->GetRootLayer()->Save())
    {
        return false;
    }

    openusd_stage* stage = nullptr;
    if (openusd_stage_open(stagePath.string().c_str(), &stage, error) !=
        OPENUSD_STATUS_OK)
    {
        return false;
    }

    const auto makeState = []()
    {
        openusd_geom_camera_state state{};
        state.struct_size = sizeof(state);
        state.version = OPENUSD_GEOM_CAMERA_STATE_VERSION;
        state.is_valid = 17;
        state.projection = 17;
        state.window_left = 17.0;
        state.window_right = 17.0;
        state.window_bottom = 17.0;
        state.window_top = 17.0;
        state.clipping_near = 17.0;
        state.clipping_far = 17.0;
        state.focal_length = 17.0;
        state.horizontal_aperture = 17.0;
        state.vertical_aperture = 17.0;
        state.horizontal_aperture_offset = 17.0;
        state.vertical_aperture_offset = 17.0;
        state.focus_distance = 17.0;
        state.f_stop = 17.0;
        return state;
    };
    const auto isCanonicalFailure = [](const openusd_geom_camera_state& state)
    {
        return state.struct_size == sizeof(state) &&
            state.version == OPENUSD_GEOM_CAMERA_STATE_VERSION &&
            state.is_valid == 0 &&
            state.projection == 0 &&
            state.window_left == 0.0 &&
            state.window_right == 0.0 &&
            state.window_bottom == 0.0 &&
            state.window_top == 0.0 &&
            state.clipping_near == 0.0 &&
            state.clipping_far == 0.0 &&
            state.focal_length == 0.0 &&
            state.horizontal_aperture == 0.0 &&
            state.vertical_aperture == 0.0 &&
            state.horizontal_aperture_offset == 0.0 &&
            state.vertical_aperture_offset == 0.0 &&
            state.focus_distance == 0.0 &&
            state.f_stop == 0.0;
    };
    const auto matchesReference =
        [&](const UsdGeomCamera& camera, int32_t sampled, double timeCode)
        {
            openusd_geom_camera_state actual = makeState();
            const openusd_status status = openusd_geom_camera_get_state(
                stage,
                camera.GetPath().GetText(),
                sampled,
                timeCode,
                &actual,
                error);
            const GfCamera expectedCamera = camera.GetCamera(
                sampled != 0
                    ? UsdTimeCode(timeCode)
                    : UsdTimeCode::Default());
            const GfFrustum expectedFrustum = expectedCamera.GetFrustum();
            const GfRange2d& expectedWindow = expectedFrustum.GetWindow();
            const GfRange1d& expectedNearFar = expectedFrustum.GetNearFar();
            const int32_t expectedProjection =
                expectedCamera.GetProjection() == GfCamera::Perspective
                ? OPENUSD_GEOM_CAMERA_PERSPECTIVE
                : OPENUSD_GEOM_CAMERA_ORTHOGRAPHIC;
            return status == OPENUSD_STATUS_OK &&
                actual.struct_size == sizeof(actual) &&
                actual.version == OPENUSD_GEOM_CAMERA_STATE_VERSION &&
                actual.is_valid == 1 &&
                actual.projection == expectedProjection &&
                actual.window_left == expectedWindow.GetMin()[0] &&
                actual.window_right == expectedWindow.GetMax()[0] &&
                actual.window_bottom == expectedWindow.GetMin()[1] &&
                actual.window_top == expectedWindow.GetMax()[1] &&
                actual.clipping_near == expectedNearFar.GetMin() &&
                actual.clipping_far == expectedNearFar.GetMax() &&
                actual.focal_length == expectedCamera.GetFocalLength() &&
                actual.horizontal_aperture ==
                    expectedCamera.GetHorizontalAperture() &&
                actual.vertical_aperture ==
                    expectedCamera.GetVerticalAperture() &&
                actual.horizontal_aperture_offset ==
                    expectedCamera.GetHorizontalApertureOffset() &&
                actual.vertical_aperture_offset ==
                    expectedCamera.GetVerticalApertureOffset() &&
                actual.focus_distance == expectedCamera.GetFocusDistance() &&
                actual.f_stop == expectedCamera.GetFStop();
        };

    if (!matchesReference(animated, 0, 0.0) ||
        !matchesReference(animated, 1, 5.0) ||
        !matchesReference(animated, 1, 10.0) ||
        !matchesReference(orthographic, 0, 0.0))
    {
        openusd_stage_release(stage);
        return false;
    }

    SetCameraStateFailpoint("after-compute");
    openusd_geom_camera_state diagnosticState = makeState();
    const openusd_status diagnosticStatus = openusd_geom_camera_get_state(
        stage,
        "/World/Animated",
        0,
        0.0,
        &diagnosticState,
        error);
    SetCameraStateFailpoint(nullptr);
    if (diagnosticStatus != OPENUSD_STATUS_NATIVE_ERROR ||
        !isCanonicalFailure(diagnosticState) ||
        error->required == 0)
    {
        openusd_stage_release(stage);
        return false;
    }

    const auto rejectsWithReset =
        [&](const openusd_stage* inputStage,
            const char* primPath,
            int32_t sampled,
            double timeCode,
            openusd_status expected)
        {
            openusd_geom_camera_state state = makeState();
            return openusd_geom_camera_get_state(
                       inputStage,
                       primPath,
                       sampled,
                       timeCode,
                       &state,
                       error) == expected &&
                isCanonicalFailure(state);
        };
    if (!rejectsWithReset(
            nullptr, "/World/Animated", 0, 0.0,
            OPENUSD_STATUS_INVALID_ARGUMENT) ||
        !rejectsWithReset(
            stage, "", 0, 0.0, OPENUSD_STATUS_INVALID_ARGUMENT) ||
        !rejectsWithReset(
            stage, "/", 0, 0.0, OPENUSD_STATUS_INVALID_ARGUMENT) ||
        !rejectsWithReset(
            stage, "/World/Missing", 0, 0.0, OPENUSD_STATUS_NOT_FOUND) ||
        !rejectsWithReset(
            stage, "/World/Inactive", 0, 0.0, OPENUSD_STATUS_NOT_FOUND) ||
        !rejectsWithReset(
            stage, "/World/WrongSchema", 0, 0.0,
            OPENUSD_STATUS_INVALID_ARGUMENT) ||
        !rejectsWithReset(
            stage, "/World/InvalidWindow", 0, 0.0,
            OPENUSD_STATUS_NATIVE_ERROR) ||
        !rejectsWithReset(
            stage, "/World/InvalidClipping", 0, 0.0,
            OPENUSD_STATUS_NATIVE_ERROR) ||
        !rejectsWithReset(
            stage, "/World/NonFinite", 0, 0.0,
            OPENUSD_STATUS_NATIVE_ERROR) ||
        !rejectsWithReset(
            stage, "/World/Malformed", 0, 0.0,
            OPENUSD_STATUS_NATIVE_ERROR) ||
        !rejectsWithReset(
            stage, "/World/PerspectiveZero", 0, 0.0,
            OPENUSD_STATUS_NATIVE_ERROR) ||
        !rejectsWithReset(
            stage, "/World/NegativeFocal", 0, 0.0,
            OPENUSD_STATUS_NATIVE_ERROR) ||
        !rejectsWithReset(
            stage, "/World/Animated", 2, 0.0,
            OPENUSD_STATUS_INVALID_ARGUMENT) ||
        !rejectsWithReset(
            stage,
            "/World/Animated",
            1,
            std::numeric_limits<double>::quiet_NaN(),
            OPENUSD_STATUS_INVALID_ARGUMENT) ||
        openusd_geom_camera_get_state(
            stage,
            "/World/Animated",
            0,
            0.0,
            nullptr,
            error) != OPENUSD_STATUS_INVALID_ARGUMENT)
    {
        openusd_stage_release(stage);
        return false;
    }

    if (sizeof(openusd_geom_camera_state) != 120 ||
        alignof(openusd_geom_camera_state) != alignof(double) ||
        offsetof(openusd_geom_camera_state, window_left) != 16 ||
        offsetof(openusd_geom_camera_state, f_stop) != 112)
    {
        openusd_stage_release(stage);
        return false;
    }

    openusd_geom_camera_state wrongVersion = makeState();
    wrongVersion.version = OPENUSD_GEOM_CAMERA_STATE_VERSION + 1;
    if (openusd_geom_camera_get_state(
            stage,
            "/World/Animated",
            0,
            0.0,
            &wrongVersion,
            error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        !isCanonicalFailure(wrongVersion))
    {
        openusd_stage_release(stage);
        return false;
    }

    openusd_geom_camera_state undersized = makeState();
    undersized.struct_size = sizeof(uint32_t) * 2;
    if (openusd_geom_camera_get_state(
            stage,
            "/World/Animated",
            0,
            0.0,
            &undersized,
            error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        undersized.struct_size != sizeof(uint32_t) * 2 ||
        undersized.version != OPENUSD_GEOM_CAMERA_STATE_VERSION ||
        undersized.is_valid != 17)
    {
        openusd_stage_release(stage);
        return false;
    }

    alignas(openusd_geom_camera_state)
        std::array<unsigned char, sizeof(openusd_geom_camera_state) + 1>
            misalignedBytes{};
    const openusd_geom_camera_state sentinel = makeState();
    std::memcpy(
        misalignedBytes.data() + 1,
        &sentinel,
        sizeof(sentinel));
    auto* misaligned =
        reinterpret_cast<openusd_geom_camera_state*>(misalignedBytes.data() + 1);
    const openusd_status misalignedStatus = openusd_geom_camera_get_state(
        stage,
        "/World/Animated",
        0,
        0.0,
        misaligned,
        error);
    openusd_geom_camera_state resetMisaligned{};
    std::memcpy(
        &resetMisaligned,
        misalignedBytes.data() + 1,
        sizeof(resetMisaligned));
    if (misalignedStatus != OPENUSD_STATUS_INVALID_ARGUMENT ||
        !isCanonicalFailure(resetMisaligned))
    {
        openusd_stage_release(stage);
        return false;
    }

    openusd_stage_release(stage);
    std::filesystem::remove(stagePath);
    return true;
}

bool VerifyCompositionEnumeration(
    const std::filesystem::path& directory,
    openusd_error_buffer* error)
{
    const std::filesystem::path strongSourcePath =
        directory / "native-enumeration-strong.usda";
    const std::filesystem::path weakSourcePath =
        directory / "native-enumeration-weak.usda";
    const std::filesystem::path deletedSourcePath =
        directory / "native-enumeration-deleted.usda";
    const std::filesystem::path tailSourcePath =
        directory / "native-enumeration-tail.usda";
    const std::filesystem::path weakLayerPath =
        directory / "native-enumeration-weak-layer.usda";
    const std::filesystem::path rootLayerPath =
        directory / "native-enumeration-root.usda";
    const std::array<std::filesystem::path, 6> files{
        strongSourcePath,
        weakSourcePath,
        deletedSourcePath,
        tailSourcePath,
        weakLayerPath,
        rootLayerPath};
    for (const std::filesystem::path& path : files)
    {
        std::filesystem::remove(path);
    }

    const auto createPayloadSource =
        [](const std::filesystem::path& path)
        {
            const UsdStageRefPtr stage = UsdStage::CreateNew(path.string());
            if (!stage)
            {
                return false;
            }
            const UsdPrim model =
                stage->DefinePrim(SdfPath("/Model"), TfToken("Xform"));
            if (!model)
            {
                return false;
            }
            stage->SetDefaultPrim(model);
            return stage->GetRootLayer()->Save();
        };
    if (!createPayloadSource(strongSourcePath) ||
        !createPayloadSource(weakSourcePath) ||
        !createPayloadSource(deletedSourcePath) ||
        !createPayloadSource(tailSourcePath))
    {
        std::cerr << "Could not create payload enumeration sources.\n";
        return false;
    }

    const std::string strongAsset = strongSourcePath.filename().generic_string();
    const std::string weakAsset = weakSourcePath.filename().generic_string();
    const std::string deletedAsset = deletedSourcePath.filename().generic_string();
    const std::string tailAsset = tailSourcePath.filename().generic_string();
    const SdfPayload strongPayload(strongAsset, SdfPath("/Model"));
    const SdfPayload weakPayload(weakAsset, SdfPath("/Model"));
    const SdfPayload deletedPayload(deletedAsset, SdfPath("/Model"));
    const SdfPayload tailPayload(tailAsset);

    const SdfLayerRefPtr anonymousLayer =
        SdfLayer::CreateAnonymous("native-enumeration-anonymous.usda");
    const UsdStageRefPtr anonymousStage = UsdStage::Open(anonymousLayer);
    if (!anonymousStage)
    {
        std::cerr << "Could not create anonymous payload source stage.\n";
        return false;
    }
    const UsdPrim anonymousModel =
        anonymousStage->DefinePrim(SdfPath("/Model"), TfToken("Xform"));
    if (!anonymousModel)
    {
        std::cerr << "Could not define anonymous payload source prim.\n";
        return false;
    }
    anonymousStage->SetDefaultPrim(anonymousModel);
    const std::string anonymousAsset = anonymousLayer->GetIdentifier();

    {
        const UsdStageRefPtr weakStage = UsdStage::CreateNew(weakLayerPath.string());
        if (!weakStage)
        {
            std::cerr << "Could not create weak enumeration layer.\n";
            return false;
        }
        const UsdPrim edited =
            weakStage->DefinePrim(SdfPath("/World/Edited"), TfToken("Xform"));
        const UsdPrim variants =
            weakStage->DefinePrim(SdfPath("/World/Variants"), TfToken("Xform"));
        if (!edited || !variants ||
            !edited.GetPayloads().AddPayload(
                deletedPayload,
                UsdListPositionFrontOfPrependList) ||
            !edited.GetPayloads().AddPayload(
                weakPayload,
                UsdListPositionBackOfPrependList) ||
            !variants.GetVariantSets().AddVariantSet("weakSet") ||
            !variants.GetVariantSets().AddVariantSet("sharedSet") ||
            !weakStage->GetRootLayer()->Save())
        {
            std::cerr << "Could not author weak enumeration opinions.\n";
            return false;
        }
    }

    {
        const UsdStageRefPtr rootStage = UsdStage::CreateNew(rootLayerPath.string());
        if (!rootStage)
        {
            std::cerr << "Could not create root enumeration layer.\n";
            return false;
        }
        rootStage->GetRootLayer()->GetSubLayerPaths().push_back(
            weakLayerPath.filename().generic_string());

        const UsdPrim edited = rootStage->OverridePrim(SdfPath("/World/Edited"));
        const UsdPrim explicitPrim =
            rootStage->DefinePrim(SdfPath("/World/Explicit"), TfToken("Xform"));
        const UsdPrim emptyPrim =
            rootStage->DefinePrim(SdfPath("/World/Empty"), TfToken("Xform"));
        const UsdPrim inactivePayload =
            rootStage->DefinePrim(SdfPath("/World/InactivePayload"), TfToken("Xform"));
        const UsdPrim variants = rootStage->OverridePrim(SdfPath("/World/Variants"));
        const UsdPrim noVariants =
            rootStage->DefinePrim(SdfPath("/World/NoVariants"), TfToken("Xform"));
        const UsdPrim inactiveVariants =
            rootStage->DefinePrim(SdfPath("/World/InactiveVariants"), TfToken("Xform"));
        static_cast<void>(emptyPrim);
        static_cast<void>(noVariants);

        SdfPayloadVector explicitPayloads{
            SdfPayload(weakAsset, SdfPath("/Model")),
            SdfPayload(anonymousAsset, SdfPath("/Model"))};
        if (!edited || !explicitPrim || !inactivePayload || !variants || !inactiveVariants ||
            !edited.GetPayloads().AddPayload(
                strongPayload,
                UsdListPositionFrontOfPrependList) ||
            !edited.GetPayloads().AddPayload(
                tailPayload,
                UsdListPositionBackOfAppendList) ||
            !edited.GetPayloads().RemovePayload(deletedPayload) ||
            !explicitPrim.GetPayloads().SetPayloads(explicitPayloads) ||
            !inactivePayload.GetPayloads().AddPayload(weakPayload) ||
            !inactivePayload.SetActive(false) ||
            !variants.GetVariantSets().AddVariantSet("strongSet") ||
            !variants.GetVariantSets().AddVariantSet("sharedSet") ||
            !inactiveVariants.GetVariantSets().AddVariantSet("inactiveSet") ||
            !inactiveVariants.SetActive(false) ||
            !rootStage->GetRootLayer()->Save())
        {
            std::cerr << "Could not author root enumeration opinions.\n";
            return false;
        }
    }

    const UsdStageRefPtr referenceStage = UsdStage::Open(rootLayerPath.string());
    openusd_stage* stage = nullptr;
    if (!referenceStage ||
        openusd_stage_open(rootLayerPath.string().c_str(), &stage, error) !=
            OPENUSD_STATUS_OK)
    {
        std::cerr << "Could not open enumeration stages: " <<
            (referenceStage ? "reference-ok" : "reference-failed") << '\n';
        openusd_stage_release(stage);
        return false;
    }

    const auto verifyPayloads =
        [&](const char* primPath, std::vector<PayloadArcResult>* actual)
        {
            const UsdPrim referencePrim =
                referenceStage->GetPrimAtPath(SdfPath(primPath));
            const std::vector<PayloadArcResult> expected =
                ReadReferencePayloadArcs(referencePrim);
            return ReadPayloadArcList(stage, primPath, error, actual) &&
                *actual == expected;
        };

    std::vector<PayloadArcResult> edited;
    std::vector<PayloadArcResult> explicitArcs;
    std::vector<PayloadArcResult> emptyArcs;
    std::vector<PayloadArcResult> inactiveArcs;
    if (!verifyPayloads("/World/Edited", &edited) ||
        edited.size() != 3 ||
        edited[0].assetPath != strongAsset ||
        edited[1].assetPath != weakAsset ||
        edited[2].assetPath != tailAsset ||
        edited[2].targetPrimPath != "" ||
        std::any_of(
            edited.begin(),
            edited.end(),
            [&](const PayloadArcResult& arc)
            {
                return arc.assetPath == deletedAsset;
            }) ||
        !verifyPayloads("/World/Explicit", &explicitArcs) ||
        explicitArcs.size() != 2 ||
        explicitArcs[0].assetPath != weakAsset ||
        explicitArcs[1].assetPath != anonymousAsset ||
        explicitArcs[1].assetPath.rfind("anon:", 0) != 0 ||
        !verifyPayloads("/World/Empty", &emptyArcs) ||
        !emptyArcs.empty() ||
        !verifyPayloads("/World/InactivePayload", &inactiveArcs) ||
        inactiveArcs.size() != 1)
    {
        std::cerr << "Payload reference mismatch: edited=" << edited.size() <<
            ", explicit=" << explicitArcs.size() <<
            ", empty=" << emptyArcs.size() <<
            ", inactive=" << inactiveArcs.size() << '\n';
        for (const PayloadArcResult& arc : edited)
        {
            std::cerr << "  edited: " << arc.assetPath << " | " <<
                arc.targetPrimPath << " | " << arc.sourceLayerIdentifier << '\n';
        }
        openusd_stage_release(stage);
        return false;
    }

    if (openusd_stage_unload_prim(stage, "/World/Edited", error) != OPENUSD_STATUS_OK)
    {
        std::cerr << "Could not unload enumeration payload prim.\n";
        openusd_stage_release(stage);
        return false;
    }
    std::vector<PayloadArcResult> unloadedArcs;
    if (!ReadPayloadArcList(stage, "/World/Edited", error, &unloadedArcs) ||
        unloadedArcs != edited)
    {
        std::cerr << "Unloaded payload arcs changed: loaded=" << edited.size() <<
            ", unloaded=" << unloadedArcs.size() << '\n';
        for (const PayloadArcResult& arc : unloadedArcs)
        {
            std::cerr << "  unloaded: " << arc.assetPath << " | " <<
                arc.targetPrimPath << " | " << arc.sourceLayerIdentifier << '\n';
        }
        openusd_stage_release(stage);
        return false;
    }

    if (openusd_stage_add_variant(
            stage,
            "/World/Variants",
            "strongSet",
            "one",
            error) != OPENUSD_STATUS_OK)
    {
        openusd_stage_release(stage);
        return false;
    }

    const auto readVariantSetNames =
        [&](const char* primPath, std::vector<std::string>* names)
        {
            return ReadPrimStringList(
                stage,
                primPath,
                openusd_stage_get_variant_set_names,
                error,
                names);
        };
    const std::vector<std::string> expectedVariantNames =
        referenceStage->GetPrimAtPath(SdfPath("/World/Variants"))
            .GetVariantSets()
            .GetNames();
    const std::vector<std::string> expectedInactiveVariantNames =
        referenceStage->GetPrimAtPath(SdfPath("/World/InactiveVariants"))
            .GetVariantSets()
            .GetNames();
    std::vector<std::string> variantNames;
    std::vector<std::string> repeatedVariantNames;
    std::vector<std::string> noVariantNames;
    std::vector<std::string> inactiveVariantNames;
    if (!readVariantSetNames("/World/Variants", &variantNames) ||
        !readVariantSetNames("/World/Variants", &repeatedVariantNames) ||
        variantNames != expectedVariantNames ||
        repeatedVariantNames != variantNames ||
        variantNames.size() != 3 ||
        !readVariantSetNames("/World/NoVariants", &noVariantNames) ||
        !noVariantNames.empty() ||
        !readVariantSetNames("/World/InactiveVariants", &inactiveVariantNames) ||
        inactiveVariantNames != expectedInactiveVariantNames ||
        inactiveVariantNames.size() != 1)
    {
        std::cerr << "Variant reference mismatch: variants=";
        for (const std::string& name : variantNames)
        {
            std::cerr << name << ',';
        }
        std::cerr << " inactive=";
        for (const std::string& name : inactiveVariantNames)
        {
            std::cerr << name << ',';
        }
        std::cerr << '\n';
        openusd_stage_release(stage);
        return false;
    }

    const auto isResetPayloadView =
        [](const openusd_payload_arc_list_view& view)
        {
            return view.struct_size == sizeof(openusd_payload_arc_list_view) &&
                view.version == OPENUSD_PAYLOAD_ARC_LIST_VIEW_VERSION &&
                view.data == nullptr && view.data_size == 0 &&
                view.offsets == nullptr && view.offsets_size == 0 &&
                view.count == 0;
        };
    const auto makePayloadSentinelView = []()
    {
        return openusd_payload_arc_list_view{
            static_cast<uint32_t>(sizeof(openusd_payload_arc_list_view)),
            OPENUSD_PAYLOAD_ARC_LIST_VIEW_VERSION,
            reinterpret_cast<const char*>(uintptr_t{1}),
            17,
            reinterpret_cast<const size_t*>(uintptr_t{1}),
            19,
            23};
    };
    const auto isResetStringView =
        [](const openusd_string_list_view& view)
        {
            return view.struct_size == sizeof(openusd_string_list_view) &&
                view.data == nullptr && view.data_size == 0 &&
                view.offsets == nullptr && view.offsets_size == 0 &&
                view.count == 0;
        };
    const auto makeStringSentinelView = []()
    {
        return openusd_string_list_view{
            static_cast<uint32_t>(sizeof(openusd_string_list_view)),
            reinterpret_cast<const char*>(uintptr_t{1}),
            29,
            reinterpret_cast<const size_t*>(uintptr_t{1}),
            31,
            37};
    };

    std::array<unsigned char, sizeof(openusd_string_list_view) + 1> misalignedBytes{};
    const openusd_string_list_view misalignedInput = makeStringSentinelView();
    std::memcpy(misalignedBytes.data() + 1, &misalignedInput, sizeof(misalignedInput));
    openusd_string_list* misalignedList =
        reinterpret_cast<openusd_string_list*>(uintptr_t{1});
    const openusd_status misalignedStatus = openusd_stage_get_variant_names(
        stage,
        "/World/Variants",
        "strongSet",
        &misalignedList,
        reinterpret_cast<openusd_string_list_view*>(misalignedBytes.data() + 1),
        error);
    openusd_string_list_view resetMisaligned{};
    std::memcpy(
        &resetMisaligned,
        misalignedBytes.data() + 1,
        sizeof(resetMisaligned));
    if (misalignedStatus != OPENUSD_STATUS_INVALID_ARGUMENT ||
        misalignedList != nullptr ||
        !isResetStringView(resetMisaligned))
    {
        std::cerr << "Misaligned variant-name view was not rejected safely.\n";
        openusd_stage_release(stage);
        return false;
    }

    openusd_payload_arc_list* payloadList =
        reinterpret_cast<openusd_payload_arc_list*>(uintptr_t{1});
    openusd_payload_arc_list_view payloadView = makePayloadSentinelView();
    if (openusd_stage_get_composed_payload_arcs(
            stage,
            "/World/Missing",
            &payloadList,
            &payloadView,
            error) != OPENUSD_STATUS_NOT_FOUND ||
        payloadList != nullptr ||
        !isResetPayloadView(payloadView))
    {
        std::cerr << "Missing payload-arc failure contract mismatch.\n";
        openusd_stage_release(stage);
        return false;
    }
    payloadList = reinterpret_cast<openusd_payload_arc_list*>(uintptr_t{1});
    payloadView = makePayloadSentinelView();
    if (openusd_stage_get_composed_payload_arcs(
            stage,
            "relative",
            &payloadList,
            &payloadView,
            error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        payloadList != nullptr ||
        !isResetPayloadView(payloadView))
    {
        std::cerr << "Invalid payload-arc path failure contract mismatch.\n";
        openusd_stage_release(stage);
        return false;
    }
    payloadList = reinterpret_cast<openusd_payload_arc_list*>(uintptr_t{1});
    payloadView = makePayloadSentinelView();
    payloadView.version = 99;
    if (openusd_stage_get_composed_payload_arcs(
            stage,
            "/World/Edited",
            &payloadList,
            &payloadView,
            error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        payloadList != nullptr ||
        !isResetPayloadView(payloadView))
    {
        std::cerr << "Payload-arc view-version failure contract mismatch.\n";
        openusd_stage_release(stage);
        return false;
    }

    openusd_string_list* stringList =
        reinterpret_cast<openusd_string_list*>(uintptr_t{1});
    openusd_string_list_view stringView = makeStringSentinelView();
    if (openusd_stage_get_variant_set_names(
            stage,
            "/World/Missing",
            &stringList,
            &stringView,
            error) != OPENUSD_STATUS_NOT_FOUND ||
        stringList != nullptr ||
        !isResetStringView(stringView))
    {
        std::cerr << "Missing variant-set failure contract mismatch.\n";
        openusd_stage_release(stage);
        return false;
    }
    stringList = reinterpret_cast<openusd_string_list*>(uintptr_t{1});
    stringView = makeStringSentinelView();
    if (openusd_stage_get_variant_set_names(
            stage,
            "relative",
            &stringList,
            &stringView,
            error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        stringList != nullptr ||
        !isResetStringView(stringView))
    {
        std::cerr << "Invalid variant-set path failure contract mismatch.\n";
        openusd_stage_release(stage);
        return false;
    }

    SetCompositionEnumerationFailpoint("payload-arcs");
    payloadList = reinterpret_cast<openusd_payload_arc_list*>(uintptr_t{1});
    payloadView = makePayloadSentinelView();
    const openusd_status payloadDiagnosticStatus =
        openusd_stage_get_composed_payload_arcs(
            stage,
            "/World/Edited",
            &payloadList,
            &payloadView,
            error);
    SetCompositionEnumerationFailpoint(nullptr);
    if (payloadDiagnosticStatus != OPENUSD_STATUS_NATIVE_ERROR ||
        payloadList != nullptr ||
        !isResetPayloadView(payloadView) ||
        error->required == 0)
    {
        std::cerr << "Payload-arc TfError failure contract mismatch: status=" <<
            payloadDiagnosticStatus << ", required=" << error->required << '\n';
        openusd_stage_release(stage);
        return false;
    }

    SetCompositionEnumerationFailpoint("variant-set-names");
    stringList = reinterpret_cast<openusd_string_list*>(uintptr_t{1});
    stringView = makeStringSentinelView();
    const openusd_status variantDiagnosticStatus =
        openusd_stage_get_variant_set_names(
            stage,
            "/World/Variants",
            &stringList,
            &stringView,
            error);
    SetCompositionEnumerationFailpoint(nullptr);
    if (variantDiagnosticStatus != OPENUSD_STATUS_NATIVE_ERROR ||
        stringList != nullptr ||
        !isResetStringView(stringView) ||
        error->required == 0)
    {
        std::cerr << "Variant-set TfError failure contract mismatch: status=" <<
            variantDiagnosticStatus << ", required=" << error->required << '\n';
        openusd_stage_release(stage);
        return false;
    }

    SetCompositionEnumerationFailpoint("payload-list-after-fill");
    payloadList = reinterpret_cast<openusd_payload_arc_list*>(uintptr_t{1});
    payloadView = makePayloadSentinelView();
    const openusd_status payloadAfterFillStatus =
        openusd_stage_get_composed_payload_arcs(
            stage,
            "/World/Edited",
            &payloadList,
            &payloadView,
            error);
    SetCompositionEnumerationFailpoint(nullptr);
    if (payloadAfterFillStatus != OPENUSD_STATUS_NATIVE_ERROR ||
        payloadList != nullptr ||
        !isResetPayloadView(payloadView) ||
        error->required == 0)
    {
        std::cerr << "Payload owner escaped a post-fill TfError: status=" <<
            payloadAfterFillStatus << ", required=" << error->required << '\n';
        openusd_stage_release(stage);
        return false;
    }

    SetCompositionEnumerationFailpoint("string-list-after-fill");
    stringList = reinterpret_cast<openusd_string_list*>(uintptr_t{1});
    stringView = makeStringSentinelView();
    const openusd_status variantSetAfterFillStatus =
        openusd_stage_get_variant_set_names(
            stage,
            "/World/Variants",
            &stringList,
            &stringView,
            error);
    SetCompositionEnumerationFailpoint(nullptr);
    if (variantSetAfterFillStatus != OPENUSD_STATUS_NATIVE_ERROR ||
        stringList != nullptr ||
        !isResetStringView(stringView) ||
        error->required == 0)
    {
        std::cerr << "Variant-set owner escaped a post-fill TfError: status=" <<
            variantSetAfterFillStatus << ", required=" << error->required << '\n';
        openusd_stage_release(stage);
        return false;
    }

    SetCompositionEnumerationFailpoint("string-list-after-fill");
    stringList = reinterpret_cast<openusd_string_list*>(uintptr_t{1});
    stringView = makeStringSentinelView();
    const openusd_status variantAfterFillStatus =
        openusd_stage_get_variant_names(
            stage,
            "/World/Variants",
            "strongSet",
            &stringList,
            &stringView,
            error);
    SetCompositionEnumerationFailpoint(nullptr);
    if (variantAfterFillStatus != OPENUSD_STATUS_NATIVE_ERROR ||
        stringList != nullptr ||
        !isResetStringView(stringView) ||
        error->required == 0)
    {
        std::cerr << "Variant-name owner escaped a post-fill TfError: status=" <<
            variantAfterFillStatus << ", required=" << error->required << '\n';
        openusd_stage_release(stage);
        return false;
    }

    openusd_payload_arc_list_release(nullptr);
    openusd_stage_release(stage);
    for (const std::filesystem::path& path : files)
    {
        std::filesystem::remove(path);
    }
    return true;
}
}

int main(int argc, char** argv)
{
    if (argc != 3)
    {
        std::cerr << "Usage: openusd_native_probe <plugin-path> <stage-path>\n";
        return 2;
    }

    if (openusd_get_abi_version() != 14 ||
        (openusd_get_capabilities() &
         (OPENUSD_CAPABILITY_STRING_LIST_V2 |
          OPENUSD_CAPABILITY_GUARDED_STATUS_EXPORTS |
          OPENUSD_CAPABILITY_SHADE_CONNECTED_SOURCES |
          OPENUSD_CAPABILITY_SHARED_STAGE_ACCESS |
          OPENUSD_CAPABILITY_WORLD_BOUNDS_QUERY |
          OPENUSD_CAPABILITY_VARIANT_SET_NAMES |
          OPENUSD_CAPABILITY_COMPOSED_DIRECT_PAYLOAD_ARCS |
          OPENUSD_CAPABILITY_WORLD_TRANSFORM_QUERY |
          OPENUSD_CAPABILITY_CAMERA_STATE_QUERY |
          OPENUSD_CAPABILITY_PCP_PRIM_INDEX_QUERY |
          OPENUSD_CAPABILITY_TS_SPLINE_QUERY |
          OPENUSD_CAPABILITY_USD_VALIDATION_QUERY |
          OPENUSD_CAPABILITY_USDGEOM_SCHEMA_COMPLETE |
          OPENUSD_CAPABILITY_USD_PHYSICS_SCHEMA |
          OPENUSD_DOTNET_CAPABILITY_USD_SHADE_SKEL |
          OPENUSD_CAPABILITY_SCHEMA_FACADES_VOL_RENDER_MEDIA_PROC_UI)) !=
            (OPENUSD_CAPABILITY_STRING_LIST_V2 |
             OPENUSD_CAPABILITY_GUARDED_STATUS_EXPORTS |
             OPENUSD_CAPABILITY_SHADE_CONNECTED_SOURCES |
             OPENUSD_CAPABILITY_SHARED_STAGE_ACCESS |
             OPENUSD_CAPABILITY_WORLD_BOUNDS_QUERY |
             OPENUSD_CAPABILITY_VARIANT_SET_NAMES |
             OPENUSD_CAPABILITY_COMPOSED_DIRECT_PAYLOAD_ARCS |
             OPENUSD_CAPABILITY_WORLD_TRANSFORM_QUERY |
             OPENUSD_CAPABILITY_CAMERA_STATE_QUERY |
             OPENUSD_CAPABILITY_PCP_PRIM_INDEX_QUERY |
             OPENUSD_CAPABILITY_TS_SPLINE_QUERY |
             OPENUSD_CAPABILITY_USD_VALIDATION_QUERY |
             OPENUSD_CAPABILITY_USDGEOM_SCHEMA_COMPLETE |
             OPENUSD_CAPABILITY_USD_PHYSICS_SCHEMA |
             OPENUSD_DOTNET_CAPABILITY_USD_SHADE_SKEL |
             OPENUSD_CAPABILITY_SCHEMA_FACADES_VOL_RENDER_MEDIA_PROC_UI))
    {
        std::cerr << "Unexpected ABI version.\n";
        return 3;
    }

    const std::string version = ReadVersion();
    if (version.empty())
    {
        std::cerr << "Could not read the OpenUSD version.\n";
        return 4;
    }
    std::cout << "OpenUSD " << version << '\n';

    std::array<char, 4096> errorText{};
    openusd_error_buffer error{errorText.data(), errorText.size(), 0};
    const auto rejectsMalformedList =
        [&](const openusd_string_list_view& malformed)
        {
            openusd_string_list_view view = malformed;
            openusd_stage* output = reinterpret_cast<openusd_stage*>(uintptr_t{1});
            errorText.fill('x');
            error.required = std::numeric_limits<size_t>::max();
            const openusd_status malformedStatus =
                openusd_stage_open_masked(argv[2], &view, &output, &error);
            return malformedStatus == OPENUSD_STATUS_INVALID_ARGUMENT &&
                output == nullptr && error.required != std::numeric_limits<size_t>::max() &&
                errorText.back() == 'x';
        };
    const size_t zeroOffset[] = {0};
    const char unterminated[] = {'/', 'A'};
    openusd_string_list_view malformed{
        sizeof(openusd_string_list_view),
        unterminated,
        sizeof(unterminated),
        zeroOffset,
        sizeof(zeroOffset),
        1};
    if (!rejectsMalformedList(malformed))
    {
        std::cerr << "Unterminated packed list was accepted.\n";
        return 101;
    }
    malformed.data = "/A";
    malformed.data_size = 3;
    malformed.offsets_size = 0;
    if (!rejectsMalformedList(malformed))
    {
        std::cerr << "Truncated offset table was accepted.\n";
        return 102;
    }
    const size_t outOfRange[] = {3};
    malformed.offsets = outOfRange;
    malformed.offsets_size = sizeof(outOfRange);
    if (!rejectsMalformedList(malformed))
    {
        std::cerr << "Out-of-range packed offset was accepted.\n";
        return 103;
    }
    alignas(size_t) std::array<unsigned char, sizeof(size_t) + 1> misalignedBytes{};
    malformed.offsets =
        reinterpret_cast<const size_t*>(misalignedBytes.data() + 1);
    if (!rejectsMalformedList(malformed))
    {
        std::cerr << "Misaligned packed offsets were accepted.\n";
        return 104;
    }
    malformed.offsets = zeroOffset;
    malformed.count = std::numeric_limits<size_t>::max();
    malformed.offsets_size = 0;
    if (!rejectsMalformedList(malformed))
    {
        std::cerr << "Impossible packed count was accepted.\n";
        return 105;
    }
    const char embedded[] = {'/', 'A', '\0', '/', 'B', '\0'};
    malformed.data = embedded;
    malformed.data_size = sizeof(embedded);
    malformed.offsets = zeroOffset;
    malformed.offsets_size = sizeof(zeroOffset);
    malformed.count = 1;
    if (!rejectsMalformedList(malformed))
    {
        std::cerr << "Packed embedded-null data was accepted.\n";
        return 106;
    }

    size_t pluginCount = 0;
    openusd_status status = openusd_register_plugins(argv[1], &pluginCount, &error);
    if (status != OPENUSD_STATUS_OK)
    {
        std::cerr << errorText.data() << '\n';
        return 5;
    }
    std::cout << "Registered plugins: " << pluginCount << '\n';

    openusd_stage* stage = nullptr;
    status = openusd_stage_open(argv[2], &stage, &error);
    if (status != OPENUSD_STATUS_OK)
    {
        std::cerr << errorText.data() << '\n';
        return 6;
    }
    if (!VerifyFailedOutputInitialization(stage, &error))
    {
        openusd_stage_release(stage);
        std::cerr << "ABI outputs retained sentinel values after failure.\n";
        return 108;
    }
    std::cout << "ABI output initialization passed.\n";
    if (!VerifySharedStageAccess(stage, argv[2], &error))
    {
        openusd_stage_release(stage);
        std::cerr << "Shared stage access contract failed: " << errorText.data() << '\n';
        return 110;
    }
    std::cout << "Shared stage access passed.\n";
    const std::filesystem::path sentinelPath =
        std::filesystem::path(argv[2]).parent_path() / "native-buffer-sentinels.usda";
    std::filesystem::remove(sentinelPath);
    openusd_stage* sentinelStage = nullptr;
    if (openusd_stage_create_new(
            sentinelPath.string().c_str(), &sentinelStage, &error) != OPENUSD_STATUS_OK ||
        !VerifyWritableBufferSentinels(sentinelStage, &error))
    {
        openusd_stage_release(sentinelStage);
        openusd_stage_release(stage);
        std::cerr << "ABI writable-buffer sentinel contract failed: " <<
            errorText.data() << '\n';
        return 109;
    }
    openusd_stage_release(sentinelStage);
    std::filesystem::remove(sentinelPath);
    std::cout << "ABI writable-buffer sentinels passed.\n";

    size_t required = 0;
    status = openusd_stage_get_root_layer_identifier(stage, nullptr, 0, &required, &error);
    if (status != OPENUSD_STATUS_BUFFER_TOO_SMALL)
    {
        openusd_stage_release(stage);
        std::cerr << errorText.data() << '\n';
        return 7;
    }

    std::vector<char> identifier(required);
    status = openusd_stage_get_root_layer_identifier(
        stage,
        identifier.data(),
        identifier.size(),
        &required,
        &error);
    openusd_stage_release(stage);
    if (status != OPENUSD_STATUS_OK)
    {
        std::cerr << errorText.data() << '\n';
        return 8;
    }

    std::cout << "Root layer: " << identifier.data() << '\n';

    const std::filesystem::path directory =
        std::filesystem::path(argv[2]).parent_path();
    if (!VerifyWorldBounds(directory, &error))
    {
        std::cerr << "World-bounds ABI contract failed: " << errorText.data() << '\n';
        return 111;
    }
    std::cout << "World-bounds ABI contract passed.\n";
    if (!VerifyWorldTransforms(directory, &error))
    {
        std::cerr << "World-transform ABI contract failed: " <<
            errorText.data() << '\n';
        return 113;
    }
    std::cout << "World-transform ABI contract passed.\n";
    if (!VerifyCameraStates(directory, &error))
    {
        std::cerr << "Camera-state ABI contract failed: " <<
            errorText.data() << '\n';
        return 114;
    }
    std::cout << "Camera-state ABI contract passed.\n";
    if (!VerifyCompositionEnumeration(directory, &error))
    {
        std::cerr << "Composition-enumeration ABI contract failed: " <<
            errorText.data() << '\n';
        return 112;
    }
    std::cout << "Composition-enumeration ABI contract passed.\n";

    const std::filesystem::path corePath = directory / "native-core-api.usda";
    const std::filesystem::path stageExportPath =
        directory / "native-core-api-flattened.usda";
    const std::filesystem::path layerExportPath =
        directory / "native-core-api-layer.usda";
    std::filesystem::remove(corePath);
    std::filesystem::remove(stageExportPath);
    std::filesystem::remove(layerExportPath);

    errorText.fill('\0');
    error.required = 0;
    stage = nullptr;
    status = openusd_stage_create_new(corePath.string().c_str(), &stage, &error);
    if (status != OPENUSD_STATUS_OK)
    {
        std::cerr << errorText.data() << '\n';
        return 9;
    }

    required = 0;
    status = openusd_stage_get_default_prim_path(stage, nullptr, 0, &required, &error);
    if (status != OPENUSD_STATUS_NOT_FOUND)
    {
        openusd_stage_release(stage);
        std::cerr << "Missing default prim did not report NOT_FOUND.\n";
        return 10;
    }

    status = openusd_stage_define_prim(stage, "/World", "Xform", &error);
    if (status != OPENUSD_STATUS_OK ||
        openusd_stage_set_default_prim(stage, "/Missing", &error) != OPENUSD_STATUS_NOT_FOUND ||
        openusd_stage_set_start_time_code(stage, 1.25, &error) != OPENUSD_STATUS_OK ||
        openusd_stage_set_end_time_code(stage, 48.5, &error) != OPENUSD_STATUS_OK ||
        openusd_stage_set_frames_per_second(stage, 30.0, &error) != OPENUSD_STATUS_OK ||
        openusd_stage_set_time_codes_per_second(stage, 60.0, &error) != OPENUSD_STATUS_OK ||
        openusd_stage_set_frames_per_second(stage, 0.0, &error) !=
            OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_stage_set_default_prim(stage, "/World", &error) != OPENUSD_STATUS_OK)
    {
        openusd_stage_release(stage);
        std::cerr << "Could not author core stage controls.\n";
        return 11;
    }

    double startTimeCode = 0;
    double endTimeCode = 0;
    double framesPerSecond = 0;
    double timeCodesPerSecond = 0;
    if (openusd_stage_get_start_time_code(stage, &startTimeCode, &error) != OPENUSD_STATUS_OK ||
        openusd_stage_get_end_time_code(stage, &endTimeCode, &error) != OPENUSD_STATUS_OK ||
        openusd_stage_get_frames_per_second(stage, &framesPerSecond, &error) != OPENUSD_STATUS_OK ||
        openusd_stage_get_time_codes_per_second(stage, &timeCodesPerSecond, &error) !=
            OPENUSD_STATUS_OK ||
        startTimeCode != 1.25 ||
        endTimeCode != 48.5 ||
        framesPerSecond != 30.0 ||
        timeCodesPerSecond != 60.0)
    {
        openusd_stage_release(stage);
        std::cerr << "Core stage timing controls did not round-trip.\n";
        return 12;
    }

    required = 0;
    status = openusd_stage_get_default_prim_path(stage, nullptr, 0, &required, &error);
    if (status != OPENUSD_STATUS_BUFFER_TOO_SMALL)
    {
        openusd_stage_release(stage);
        std::cerr << "Could not size the default prim path.\n";
        return 13;
    }
    std::vector<char> defaultPrim(required);
    status = openusd_stage_get_default_prim_path(
        stage,
        defaultPrim.data(),
        defaultPrim.size(),
        &required,
        &error);
    if (status != OPENUSD_STATUS_OK || std::string(defaultPrim.data()) != "/World")
    {
        openusd_stage_release(stage);
        std::cerr << "Default prim did not round-trip.\n";
        return 14;
    }

    openusd_layer* sessionLayer = nullptr;
    status = openusd_stage_get_session_layer(stage, &sessionLayer, &error);
    if (status != OPENUSD_STATUS_OK)
    {
        openusd_stage_release(stage);
        std::cerr << errorText.data() << '\n';
        return 15;
    }

    required = 0;
    status = openusd_stage_get_session_layer_identifier(
        stage,
        nullptr,
        0,
        &required,
        &error);
    if (status != OPENUSD_STATUS_BUFFER_TOO_SMALL)
    {
        openusd_layer_release(sessionLayer);
        openusd_stage_release(stage);
        std::cerr << "Could not size the session layer identifier.\n";
        return 16;
    }
    std::vector<char> sessionIdentifier(required);
    status = openusd_stage_get_session_layer_identifier(
        stage,
        sessionIdentifier.data(),
        sessionIdentifier.size(),
        &required,
        &error);
    openusd_layer_release(sessionLayer);
    if (status != OPENUSD_STATUS_OK || sessionIdentifier[0] == '\0')
    {
        openusd_stage_release(stage);
        std::cerr << "Session layer identifier was not returned.\n";
        return 17;
    }

    openusd_layer* rootLayer = nullptr;
    int32_t reloaded = 0;
    if (openusd_stage_get_root_layer(stage, &rootLayer, &error) != OPENUSD_STATUS_OK ||
        openusd_stage_save(stage, &error) != OPENUSD_STATUS_OK ||
        openusd_layer_export(rootLayer, layerExportPath.string().c_str(), &error) !=
            OPENUSD_STATUS_OK ||
        openusd_layer_reload(rootLayer, 1, &reloaded, &error) != OPENUSD_STATUS_OK ||
        reloaded == 0 ||
        openusd_stage_export(stage, stageExportPath.string().c_str(), &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_reload(stage, &error) != OPENUSD_STATUS_OK)
    {
        openusd_layer_release(rootLayer);
        openusd_stage_release(stage);
        std::cerr << errorText.data() << '\n';
        return 18;
    }
    openusd_layer_release(rootLayer);

    if (openusd_stage_clear_default_prim(stage, &error) != OPENUSD_STATUS_OK ||
        openusd_stage_get_default_prim_path(stage, nullptr, 0, &required, &error) !=
            OPENUSD_STATUS_NOT_FOUND)
    {
        openusd_stage_release(stage);
        std::cerr << "Default prim was not cleared.\n";
        return 19;
    }
    openusd_stage_release(stage);

    if (!std::filesystem::exists(stageExportPath) ||
        !std::filesystem::exists(layerExportPath))
    {
        std::cerr << "Core stage or layer export was not created.\n";
        return 20;
    }

    std::cout << "Core stage controls passed.\n";

    const std::filesystem::path inspectionPath =
        directory / "native-scene-inspection.usda";
    std::filesystem::remove(inspectionPath);
    stage = nullptr;
    status = openusd_stage_create_new(inspectionPath.string().c_str(), &stage, &error);
    if (status != OPENUSD_STATUS_OK ||
        openusd_stage_define_prim(stage, "/World", "Xform", &error) != OPENUSD_STATUS_OK ||
        openusd_stage_define_prim(stage, "/World/Defined", "Xform", &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_override_prim(stage, "/World/Over", &error) != OPENUSD_STATUS_OK ||
        openusd_stage_create_class_prim(stage, "/Template", &error) != OPENUSD_STATUS_OK ||
        openusd_stage_create_class_prim(stage, "/World/NestedClass", &error) !=
            OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_stage_set_double(
            stage,
            "/World/Defined",
            "custom:value",
            7.5,
            0,
            0,
            &error) != OPENUSD_STATUS_OK ||
        openusd_stage_create_relationship(
            stage,
            "/World/Defined",
            "custom:link",
            &error) != OPENUSD_STATUS_OK ||
        openusd_stage_save(stage, &error) != OPENUSD_STATUS_OK)
    {
        openusd_stage_release(stage);
        std::cerr << "Could not author scene inspection data.\n";
        return 21;
    }
    openusd_stage_release(stage);

    stage = nullptr;
    status = openusd_stage_open(inspectionPath.string().c_str(), &stage, &error);
    if (status != OPENUSD_STATUS_OK)
    {
        std::cerr << errorText.data() << '\n';
        return 22;
    }

    required = 0;
    status = openusd_stage_get_prim_type_name(
        stage,
        "/World/Defined",
        nullptr,
        0,
        &required,
        &error);
    std::vector<char> typeName(required);
    if (status != OPENUSD_STATUS_BUFFER_TOO_SMALL ||
        openusd_stage_get_prim_type_name(
            stage,
            "/World/Defined",
            typeName.data(),
            typeName.size(),
            &required,
            &error) != OPENUSD_STATUS_OK ||
        std::string(typeName.data()) != "Xform" ||
        openusd_stage_get_prim_type_name(stage, "/Missing", nullptr, 0, &required, &error) !=
            OPENUSD_STATUS_NOT_FOUND ||
        openusd_stage_get_prim_type_name(stage, "relative", nullptr, 0, &required, &error) !=
            OPENUSD_STATUS_INVALID_ARGUMENT)
    {
        openusd_stage_release(stage);
        std::cerr << "Prim type inspection or explicit errors failed.\n";
        return 23;
    }

    std::vector<std::string> childPaths;
    std::vector<std::string> attributeNames;
    std::vector<std::string> relationshipNames;
    std::vector<std::string> appliedSchemas;
    if (!ReadPrimStringList(
            stage,
            "/World",
            openusd_stage_get_prim_child_paths,
            &error,
            &childPaths) ||
        !ReadPrimStringList(
            stage,
            "/World/Defined",
            openusd_stage_get_prim_attribute_names,
            &error,
            &attributeNames) ||
        !ReadPrimStringList(
            stage,
            "/World/Defined",
            openusd_stage_get_prim_relationship_names,
            &error,
            &relationshipNames) ||
        !ReadPrimStringList(
            stage,
            "/World/Defined",
            openusd_stage_get_prim_applied_schemas,
            &error,
            &appliedSchemas) ||
        std::find(childPaths.begin(), childPaths.end(), "/World/Defined") == childPaths.end() ||
        std::find(childPaths.begin(), childPaths.end(), "/World/Over") == childPaths.end() ||
        std::find(attributeNames.begin(), attributeNames.end(), "custom:value") ==
            attributeNames.end() ||
        std::find(relationshipNames.begin(), relationshipNames.end(), "custom:link") ==
            relationshipNames.end())
    {
        openusd_stage_release(stage);
        std::cerr << "Bulk scene inspection did not round-trip.\n";
        return 24;
    }

    openusd_string_list* missingList = nullptr;
    openusd_string_list_view missingView{
        static_cast<uint32_t>(sizeof(openusd_string_list_view)), nullptr, 0, nullptr, 0, 0};
    if (openusd_stage_get_prim_child_paths(
            stage,
            "/Missing",
            &missingList,
            &missingView,
            &error) != OPENUSD_STATUS_NOT_FOUND)
    {
        openusd_stage_release(stage);
        std::cerr << "Missing prim bulk inspection did not report NOT_FOUND.\n";
        return 25;
    }

    openusd_stage_release(stage);
    std::cout << "Bulk scene inspection passed.\n";

    const std::filesystem::path propertyPath =
        directory / "native-property-model.usda";
    std::filesystem::remove(propertyPath);
    stage = nullptr;
    const openusd_vec3f vectorValue{1.0F, 2.0F, 3.0F};
    const openusd_vec3f colorValue{0.25F, 0.5F, 0.75F};
    const double arrayValue[] = {1.0, 2.0};
    status = openusd_stage_create_new(propertyPath.string().c_str(), &stage, &error);
    if (status != OPENUSD_STATUS_OK ||
        openusd_stage_define_prim(stage, "/World/Properties", "Xform", &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_bool(
            stage, "/World/Properties", "custom:enabled", 1, 0, 0, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_int64(
            stage, "/World/Properties", "custom:count", 42, 0, 0, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_double(
            stage, "/World/Properties", "custom:number", 3.5, 0, 0, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_double(
            stage, "/World/Properties", "custom:number", 4.5, 1, 1.0, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_double(
            stage, "/World/Properties", "custom:number", 5.5, 1, 2.0, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_string(
            stage, "/World/Properties", "custom:label", "hello", 0, 0, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_token(
            stage, "/World/Properties", "custom:kind", "Beacon", 0, 0, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_vec3f(
            stage, "/World/Properties", "custom:vector", &vectorValue, 0, 0, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_color3f(
            stage, "/World/Properties", "custom:color", &colorValue, 0, 0, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_double_array(
            stage,
            "/World/Properties",
            "custom:array",
            arrayValue,
            2,
            0,
            0,
            &error) != OPENUSD_STATUS_OK ||
        openusd_stage_save(stage, &error) != OPENUSD_STATUS_OK)
    {
        openusd_stage_release(stage);
        std::cerr << "Could not author property model data.\n";
        return 26;
    }
    openusd_stage_release(stage);

    stage = nullptr;
    status = openusd_stage_open(propertyPath.string().c_str(), &stage, &error);
    if (status != OPENUSD_STATUS_OK)
    {
        std::cerr << errorText.data() << '\n';
        return 27;
    }

    required = 0;
    status = openusd_stage_get_attribute_type_name(
        stage,
        "/World/Properties",
        "custom:number",
        nullptr,
        0,
        &required,
        &error);
    std::vector<char> attributeType(required);
    if (status != OPENUSD_STATUS_BUFFER_TOO_SMALL ||
        openusd_stage_get_attribute_type_name(
            stage,
            "/World/Properties",
            "custom:number",
            attributeType.data(),
            attributeType.size(),
            &required,
            &error) != OPENUSD_STATUS_OK ||
        std::string(attributeType.data()) != "double")
    {
        openusd_stage_release(stage);
        std::cerr << "Attribute type name did not round-trip.\n";
        return 28;
    }

    required = 0;
    status = openusd_stage_get_attribute_time_samples(
        stage,
        "/World/Properties",
        "custom:number",
        nullptr,
        0,
        &required,
        &error);
    std::vector<double> sampleTimes(required);
    if (status != OPENUSD_STATUS_OK ||
        openusd_stage_get_attribute_time_samples(
            stage,
            "/World/Properties",
            "custom:number",
            sampleTimes.data(),
            sampleTimes.size(),
            &required,
            &error) != OPENUSD_STATUS_OK ||
        sampleTimes != std::vector<double>({1.0, 2.0}))
    {
        openusd_stage_release(stage);
        std::cerr << "Attribute time samples did not round-trip in bulk.\n";
        return 29;
    }

    openusd_scalar_value scalar{};
    std::string text;
    if (!ReadScalar(
            stage, "/World/Properties", "custom:enabled", 0, 0, &error, &scalar, &text) ||
        scalar.kind != OPENUSD_SCALAR_KIND_BOOL ||
        scalar.bool_value == 0 ||
        !ReadScalar(
            stage, "/World/Properties", "custom:count", 0, 0, &error, &scalar, &text) ||
        scalar.kind != OPENUSD_SCALAR_KIND_INT64 ||
        scalar.int64_value != 42 ||
        !ReadScalar(
            stage, "/World/Properties", "custom:number", 1, 2.0, &error, &scalar, &text) ||
        scalar.kind != OPENUSD_SCALAR_KIND_DOUBLE ||
        scalar.double_value != 5.5 ||
        !ReadScalar(
            stage, "/World/Properties", "custom:label", 0, 0, &error, &scalar, &text) ||
        scalar.kind != OPENUSD_SCALAR_KIND_STRING ||
        text != "hello" ||
        !ReadScalar(
            stage, "/World/Properties", "custom:kind", 0, 0, &error, &scalar, &text) ||
        scalar.kind != OPENUSD_SCALAR_KIND_TOKEN ||
        text != "Beacon" ||
        !ReadScalar(
            stage, "/World/Properties", "custom:vector", 0, 0, &error, &scalar, &text) ||
        scalar.kind != OPENUSD_SCALAR_KIND_VEC3F ||
        scalar.vec3f_value.x != 1.0F ||
        scalar.vec3f_value.y != 2.0F ||
        scalar.vec3f_value.z != 3.0F ||
        !ReadScalar(
            stage, "/World/Properties", "custom:color", 0, 0, &error, &scalar, &text) ||
        scalar.kind != OPENUSD_SCALAR_KIND_COLOR3F ||
        scalar.vec3f_value.x != 0.25F ||
        scalar.vec3f_value.y != 0.5F ||
        scalar.vec3f_value.z != 0.75F)
    {
        openusd_stage_release(stage);
        std::cerr << "Tagged scalar values did not round-trip.\n";
        return 30;
    }

    scalar = {};
    scalar.struct_size = static_cast<uint32_t>(sizeof(openusd_scalar_value));
    size_t stringRequired = 0;
    int32_t ignoredBool = 0;
    if (openusd_stage_get_attribute_scalar_value(
            stage,
            "/World/Properties",
            "custom:array",
            0,
            0,
            &scalar,
            nullptr,
            0,
            &stringRequired,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_stage_get_bool(
            stage,
            "/World/Properties",
            "custom:number",
            0,
            0,
            &ignoredBool,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_stage_get_attribute_value_state(
            stage,
            "/World/Properties",
            "missing",
            0,
            0,
            &ignoredBool,
            &ignoredBool,
            &error) != OPENUSD_STATUS_NOT_FOUND)
    {
        openusd_stage_release(stage);
        std::cerr << "Property type mismatch or missing-property errors failed.\n";
        return 31;
    }

    int32_t hasAuthoredValue = 0;
    int32_t isBlocked = 0;
    if (openusd_stage_block_attribute_value(
            stage, "/World/Properties", "custom:number", &error) != OPENUSD_STATUS_OK ||
        openusd_stage_get_attribute_value_state(
            stage,
            "/World/Properties",
            "custom:number",
            0,
            0,
            &hasAuthoredValue,
            &isBlocked,
            &error) != OPENUSD_STATUS_OK ||
        hasAuthoredValue == 0 ||
        isBlocked == 0 ||
        openusd_stage_get_attribute_scalar_value(
            stage,
            "/World/Properties",
            "custom:number",
            0,
            0,
            &scalar,
            nullptr,
            0,
            &stringRequired,
            &error) != OPENUSD_STATUS_NOT_FOUND ||
        openusd_stage_clear_attribute_value(
            stage, "/World/Properties", "custom:number", &error) != OPENUSD_STATUS_OK ||
        openusd_stage_get_attribute_value_state(
            stage,
            "/World/Properties",
            "custom:number",
            0,
            0,
            &hasAuthoredValue,
            &isBlocked,
            &error) != OPENUSD_STATUS_OK ||
        hasAuthoredValue != 0 ||
        isBlocked != 0)
    {
        openusd_stage_release(stage);
        std::cerr << "Attribute block or clear state failed.\n";
        return 32;
    }

    openusd_stage_release(stage);
    std::cout << "Property model passed.\n";

    const std::filesystem::path geometryPath =
        directory / "native-geometry-values.usda";
    std::filesystem::remove(geometryPath);
    const int32_t faceCounts[] = {4};
    const int32_t faceIndices[] = {0, 1, 2, 3};
    const openusd_vec3f points[] = {
        {-1.0F, -1.0F, 0.0F},
        {1.0F, -1.0F, 0.0F},
        {1.0F, 1.0F, 0.0F},
        {-1.0F, 1.0F, 0.0F}};
    const openusd_vec3f sampledPoints[] = {
        {-2.0F, -1.0F, 0.0F},
        {2.0F, -1.0F, 0.0F},
        {2.0F, 1.0F, 0.0F},
        {-2.0F, 1.0F, 0.0F}};
    const openusd_vec2f uvs[] = {
        {0.0F, 0.0F},
        {1.0F, 0.0F},
        {1.0F, 1.0F},
        {0.0F, 1.0F}};
    const openusd_matrix4d transform{{
        1.0, 0.0, 0.0, 0.0,
        0.0, 1.0, 0.0, 0.0,
        0.0, 0.0, 1.0, 0.0,
        10.0, 20.0, 30.0, 1.0}};
    std::vector<float> largeWeights(65536);
    for (size_t index = 0; index < largeWeights.size(); ++index)
    {
        largeWeights[index] = static_cast<float>(index) * 0.5F;
    }

    stage = nullptr;
    status = openusd_stage_create_new(geometryPath.string().c_str(), &stage, &error);
    if (status != OPENUSD_STATUS_OK ||
        openusd_stage_define_prim(stage, "/World/Mesh", "Xform", &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_int32_array(
            stage, "/World/Mesh", "faceVertexCounts", faceCounts, 1, 0, 0, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_int32_array(
            stage, "/World/Mesh", "faceVertexIndices", faceIndices, 4, 0, 0, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_vec3f_array(
            stage, "/World/Mesh", "points", points, 4, 0, 0, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_vec3f_array(
            stage, "/World/Mesh", "points", sampledPoints, 4, 1, 10.0, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_vec2f_array(
            stage, "/World/Mesh", "custom:uvs", uvs, 4, 0, 0, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_vec2f_array(
            stage, "/World/Mesh", "custom:emptyUvs", nullptr, 0, 0, 0, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_float_array(
            stage,
            "/World/Mesh",
            "custom:weights",
            largeWeights.data(),
            largeWeights.size(),
            0,
            0,
            &error) != OPENUSD_STATUS_OK ||
        openusd_stage_set_matrix4d(
            stage, "/World/Mesh", "xformOp:transform", &transform, 0, 0, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_save(stage, &error) != OPENUSD_STATUS_OK)
    {
        openusd_stage_release(stage);
        std::cerr << "Could not author contiguous geometry values: " << errorText.data() << '\n';
        return 60;
    }

    std::vector<int32_t> readCounts;
    std::vector<int32_t> readIndices;
    std::vector<float> readWeights;
    std::vector<openusd_vec2f> readUvs;
    std::vector<openusd_vec2f> readEmptyUvs;
    std::vector<openusd_vec3f> readPoints;
    openusd_matrix4d readTransform{};
    if (!ReadArray(
            stage,
            "/World/Mesh",
            "faceVertexCounts",
            0,
            0,
            openusd_stage_get_int32_array,
            &error,
            &readCounts) ||
        !ReadArray(
            stage,
            "/World/Mesh",
            "faceVertexIndices",
            0,
            0,
            openusd_stage_get_int32_array,
            &error,
            &readIndices) ||
        !ReadArray(
            stage,
            "/World/Mesh",
            "custom:weights",
            0,
            0,
            openusd_stage_get_float_array,
            &error,
            &readWeights) ||
        !ReadArray(
            stage,
            "/World/Mesh",
            "custom:uvs",
            0,
            0,
            openusd_stage_get_vec2f_array,
            &error,
            &readUvs) ||
        !ReadArray(
            stage,
            "/World/Mesh",
            "custom:emptyUvs",
            0,
            0,
            openusd_stage_get_vec2f_array,
            &error,
            &readEmptyUvs) ||
        !ReadArray(
            stage,
            "/World/Mesh",
            "points",
            1,
            10.0,
            openusd_stage_get_vec3f_array,
            &error,
            &readPoints) ||
        openusd_stage_get_matrix4d(
            stage, "/World/Mesh", "xformOp:transform", 0, 0, &readTransform, &error) !=
            OPENUSD_STATUS_OK ||
        readCounts != std::vector<int32_t>({4}) ||
        readIndices != std::vector<int32_t>({0, 1, 2, 3}) ||
        readWeights != largeWeights ||
        readUvs.size() != 4 ||
        !readEmptyUvs.empty() ||
        readPoints.size() != 4 ||
        readPoints[0].x != -2.0F ||
        readPoints[3].y != 1.0F ||
        readTransform.values[12] != 10.0 ||
        readTransform.values[13] != 20.0 ||
        readTransform.values[14] != 30.0)
    {
        openusd_stage_release(stage);
        std::cerr << "Contiguous geometry values did not round-trip.\n";
        return 61;
    }

    scalar = {};
    scalar.struct_size = static_cast<uint32_t>(sizeof(openusd_scalar_value));
    stringRequired = 0;
    if (openusd_stage_get_attribute_scalar_value(
            stage,
            "/World/Mesh",
            "xformOp:transform",
            0,
            0,
            &scalar,
            nullptr,
            0,
            &stringRequired,
            &error) != OPENUSD_STATUS_OK ||
        scalar.kind != OPENUSD_SCALAR_KIND_MATRIX4D ||
        scalar.matrix4d_value.values[12] != 10.0)
    {
        openusd_stage_release(stage);
        std::cerr << "Tagged matrix4d value did not round-trip.\n";
        return 62;
    }

    alignas(float) std::array<unsigned char, sizeof(float) + 1> malformedBuffer{};
    float oneFloat = 1.0F;
    int32_t undersizedOutput = 0;
    size_t malformedRequired = 0;
    if (openusd_stage_set_float_array(
            stage,
            "/World/Mesh",
            "custom:bad",
            reinterpret_cast<const float*>(malformedBuffer.data() + 1),
            1,
            0,
            0,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_stage_set_float_array(
            stage,
            "/World/Mesh",
            "custom:overflow",
            &oneFloat,
            std::numeric_limits<size_t>::max(),
            0,
            0,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_stage_set_float_array(
            stage, "/World/Mesh", "custom:null", nullptr, 1, 0, 0, &error) !=
            OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_stage_get_int32_array(
            stage,
            "/World/Mesh",
            "faceVertexIndices",
            0,
            0,
            &undersizedOutput,
            1,
            &malformedRequired,
            &error) != OPENUSD_STATUS_BUFFER_TOO_SMALL ||
        malformedRequired != 0 || undersizedOutput != 0 ||
        openusd_stage_get_float_array(
            stage,
            "/World/Mesh",
            "faceVertexCounts",
            0,
            0,
            nullptr,
            0,
            &malformedRequired,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT)
    {
        openusd_stage_release(stage);
        std::cerr << "Geometry buffer validation or mismatch errors failed.\n";
        return 63;
    }
    openusd_stage_release(stage);

    stage = nullptr;
    status = openusd_stage_open(geometryPath.string().c_str(), &stage, &error);
    readPoints.clear();
    if (status != OPENUSD_STATUS_OK ||
        !ReadArray(
            stage,
            "/World/Mesh",
            "points",
            0,
            0,
            openusd_stage_get_vec3f_array,
            &error,
            &readPoints) ||
        readPoints.size() != 4 ||
        readPoints[0].x != -1.0F)
    {
        openusd_stage_release(stage);
        std::cerr << "Saved geometry values did not round-trip.\n";
        return 64;
    }
    openusd_stage_release(stage);
    std::cout << "Geometry values passed.\n";

    const std::filesystem::path usdGeomPath = directory / "native-usdgeom.usda";
    std::filesystem::remove(usdGeomPath);
    stage = nullptr;
    const int32_t geomCounts[] = {4};
    const int32_t geomIndices[] = {0, 1, 2, 3};
    const openusd_vec3f geomPoints[] = {
        {-1.0F, -1.0F, 0.0F},
        {1.0F, -1.0F, 0.0F},
        {1.0F, 1.0F, 0.0F},
        {-1.0F, 1.0F, 0.0F}};
    const openusd_vec3f sampledGeomPoints[] = {
        {-2.0F, -1.0F, 0.0F},
        {2.0F, -1.0F, 0.0F},
        {2.0F, 1.0F, 0.0F},
        {-2.0F, 1.0F, 0.0F}};
    const openusd_vec3f geomNormals[] = {
        {0.0F, 0.0F, 1.0F},
        {0.0F, 0.0F, 1.0F},
        {0.0F, 0.0F, 1.0F},
        {0.0F, 0.0F, 1.0F}};
    const openusd_extent3f geomExtent{
        {-2.0F, -1.0F, 0.0F},
        {2.0F, 1.0F, 0.0F}};
    const openusd_matrix4d xformMatrix{{
        1.0, 0.0, 0.0, 0.0,
        0.0, 1.0, 0.0, 0.0,
        0.0, 0.0, 1.0, 0.0,
        5.0, 6.0, 7.0, 1.0}};
    const openusd_matrix4d cameraMatrix{{
        1.0, 0.0, 0.0, 0.0,
        0.0, 1.0, 0.0, 0.0,
        0.0, 0.0, 1.0, 0.0,
        0.0, 2.0, 10.0, 1.0}};
    const openusd_vec3f oneGeomNormal[] = {{0.0F, 0.0F, 1.0F}};
    const openusd_vec2f clippingRange{0.1F, 1000.0F};
    status = openusd_stage_create_new(usdGeomPath.string().c_str(), &stage, &error);
    if (status != OPENUSD_STATUS_OK ||
        openusd_geom_define_xform(stage, "/World", &error) != OPENUSD_STATUS_OK ||
        openusd_geom_define_mesh(stage, "/World/Mesh", &error) != OPENUSD_STATUS_OK ||
        openusd_geom_define_camera(stage, "/World/Camera", &error) != OPENUSD_STATUS_OK ||
        openusd_geom_imageable_set_visibility(
            stage,
            "/World/Mesh",
            OPENUSD_GEOM_VISIBILITY_INVISIBLE,
            0,
            0,
            &error) != OPENUSD_STATUS_OK ||
        openusd_geom_imageable_set_purpose(
            stage, "/World", OPENUSD_GEOM_PURPOSE_RENDER, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_xformable_set_local_transform(
            stage, "/World", &xformMatrix, 0, 0, &error) != OPENUSD_STATUS_OK ||
        openusd_geom_xformable_set_reset_xform_stack(
            stage, "/World", 1, &error) != OPENUSD_STATUS_OK ||
        openusd_geom_mesh_set_topology(
            stage,
            "/World/Mesh",
            geomCounts,
            1,
            geomIndices,
            4,
            &error) != OPENUSD_STATUS_OK ||
        openusd_geom_mesh_set_points(
            stage, "/World/Mesh", geomPoints, 4, 0, 0, &error) != OPENUSD_STATUS_OK ||
        openusd_geom_mesh_set_points(
            stage, "/World/Mesh", sampledGeomPoints, 4, 1, 10.0, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_mesh_set_normals(
            stage,
            "/World/Mesh",
            oneGeomNormal,
            1,
            OPENUSD_GEOM_INTERPOLATION_CONSTANT,
            0,
            0,
            &error) != OPENUSD_STATUS_OK ||
        openusd_geom_mesh_set_normals(
            stage,
            "/World/Mesh",
            oneGeomNormal,
            1,
            OPENUSD_GEOM_INTERPOLATION_UNIFORM,
            0,
            0,
            &error) != OPENUSD_STATUS_OK ||
        openusd_geom_mesh_set_normals(
            stage,
            "/World/Mesh",
            geomNormals,
            4,
            OPENUSD_GEOM_INTERPOLATION_VARYING,
            0,
            0,
            &error) != OPENUSD_STATUS_OK ||
        openusd_geom_mesh_set_normals(
            stage,
            "/World/Mesh",
            geomNormals,
            4,
            OPENUSD_GEOM_INTERPOLATION_FACE_VARYING,
            0,
            0,
            &error) != OPENUSD_STATUS_OK ||
        openusd_geom_mesh_set_normals(
            stage,
            "/World/Mesh",
            geomNormals,
            4,
            OPENUSD_GEOM_INTERPOLATION_VERTEX,
            0,
            0,
            &error) != OPENUSD_STATUS_OK ||
        openusd_geom_mesh_set_subdivision_scheme(
            stage, "/World/Mesh", OPENUSD_GEOM_SUBDIVISION_NONE, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_mesh_set_orientation(
            stage, "/World/Mesh", OPENUSD_GEOM_ORIENTATION_LEFT_HANDED, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_mesh_set_double_sided(stage, "/World/Mesh", 1, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_mesh_set_extent(
            stage, "/World/Mesh", &geomExtent, 0, 0, &error) != OPENUSD_STATUS_OK ||
        openusd_geom_camera_set_projection(
            stage, "/World/Camera", OPENUSD_GEOM_CAMERA_ORTHOGRAPHIC, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_camera_set_float_property(
            stage,
            "/World/Camera",
            OPENUSD_GEOM_CAMERA_FOCAL_LENGTH,
            0.0F,
            &error) != OPENUSD_STATUS_OK ||
        openusd_geom_camera_set_float_property(
            stage,
            "/World/Camera",
            OPENUSD_GEOM_CAMERA_HORIZONTAL_APERTURE,
            24.0F,
            &error) != OPENUSD_STATUS_OK ||
        openusd_geom_camera_set_float_property(
            stage,
            "/World/Camera",
            OPENUSD_GEOM_CAMERA_VERTICAL_APERTURE,
            18.0F,
            &error) != OPENUSD_STATUS_OK ||
        openusd_geom_camera_set_clipping_range(
            stage, "/World/Camera", &clippingRange, &error) != OPENUSD_STATUS_OK ||
        openusd_geom_xformable_set_local_transform(
            stage, "/World/Camera", &cameraMatrix, 0, 0, &error) != OPENUSD_STATUS_OK ||
        openusd_stage_save(stage, &error) != OPENUSD_STATUS_OK)
    {
        openusd_stage_release(stage);
        std::cerr << "Could not author native UsdGeom data: " << errorText.data() << '\n';
        return 65;
    }

    int32_t matchesSchema = 0;
    int32_t enumValue = -1;
    int32_t boolValue = 0;
    float cameraValue = 0.0F;
    openusd_matrix4d readLocalTransform{};
    openusd_extent3f readGeomExtent{};
    openusd_vec2f readClippingRange{};
    std::vector<int32_t> readGeomCounts;
    std::vector<int32_t> readGeomIndices;
    std::vector<openusd_vec3f> readGeomPoints;
    std::vector<openusd_vec3f> readGeomNormals;
    if (openusd_geom_is_schema(
            stage,
            "/World/Mesh",
            OPENUSD_GEOM_SCHEMA_MESH,
            &matchesSchema,
            &error) != OPENUSD_STATUS_OK ||
        matchesSchema == 0 ||
        openusd_geom_is_schema(
            stage,
            "/World",
            OPENUSD_GEOM_SCHEMA_MESH,
            &matchesSchema,
            &error) != OPENUSD_STATUS_OK ||
        matchesSchema != 0 ||
        openusd_geom_imageable_get_visibility(
            stage, "/World/Mesh", 0, 0, &enumValue, &error) != OPENUSD_STATUS_OK ||
        enumValue != OPENUSD_GEOM_VISIBILITY_INVISIBLE ||
        openusd_geom_imageable_get_purpose(
            stage, "/World/Mesh", &enumValue, &error) != OPENUSD_STATUS_OK ||
        enumValue != OPENUSD_GEOM_PURPOSE_RENDER ||
        openusd_geom_xformable_get_local_transform(
            stage, "/World", 0, 0, &readLocalTransform, &error) != OPENUSD_STATUS_OK ||
        readLocalTransform.values[12] != 5.0 ||
        openusd_geom_xformable_get_reset_xform_stack(
            stage, "/World", &boolValue, &error) != OPENUSD_STATUS_OK ||
        boolValue == 0 ||
        !ReadGeomArray(
            stage,
            "/World/Mesh",
            openusd_geom_mesh_get_face_vertex_counts,
            &error,
            &readGeomCounts) ||
        !ReadGeomArray(
            stage,
            "/World/Mesh",
            openusd_geom_mesh_get_face_vertex_indices,
            &error,
            &readGeomIndices) ||
        !ReadGeomArray(
            stage,
            "/World/Mesh",
            1,
            10.0,
            openusd_geom_mesh_get_points,
            &error,
            &readGeomPoints) ||
        !ReadGeomArray(
            stage,
            "/World/Mesh",
            0,
            0,
            openusd_geom_mesh_get_normals,
            &error,
            &readGeomNormals) ||
        readGeomCounts != std::vector<int32_t>({4}) ||
        readGeomIndices != std::vector<int32_t>({0, 1, 2, 3}) ||
        readGeomPoints.size() != 4 ||
        readGeomPoints[0].x != -2.0F ||
        readGeomNormals.size() != 4 ||
        openusd_geom_mesh_get_normals_interpolation(
            stage, "/World/Mesh", &enumValue, &error) != OPENUSD_STATUS_OK ||
        enumValue != OPENUSD_GEOM_INTERPOLATION_VERTEX ||
        openusd_geom_mesh_get_subdivision_scheme(
            stage, "/World/Mesh", &enumValue, &error) != OPENUSD_STATUS_OK ||
        enumValue != OPENUSD_GEOM_SUBDIVISION_NONE ||
        openusd_geom_mesh_get_orientation(
            stage, "/World/Mesh", &enumValue, &error) != OPENUSD_STATUS_OK ||
        enumValue != OPENUSD_GEOM_ORIENTATION_LEFT_HANDED ||
        openusd_geom_mesh_get_double_sided(
            stage, "/World/Mesh", &boolValue, &error) != OPENUSD_STATUS_OK ||
        boolValue == 0 ||
        openusd_geom_mesh_get_extent(
            stage, "/World/Mesh", 0, 0, &readGeomExtent, &error) != OPENUSD_STATUS_OK ||
        readGeomExtent.maximum.x != 2.0F ||
        openusd_geom_camera_get_projection(
            stage, "/World/Camera", &enumValue, &error) != OPENUSD_STATUS_OK ||
        enumValue != OPENUSD_GEOM_CAMERA_ORTHOGRAPHIC ||
        openusd_geom_camera_get_float_property(
            stage,
            "/World/Camera",
            OPENUSD_GEOM_CAMERA_FOCAL_LENGTH,
            &cameraValue,
            &error) != OPENUSD_STATUS_OK ||
        cameraValue != 0.0F ||
        openusd_geom_camera_get_clipping_range(
            stage, "/World/Camera", &readClippingRange, &error) != OPENUSD_STATUS_OK ||
        readClippingRange.x != 0.1F ||
        readClippingRange.y != 1000.0F)
    {
        openusd_stage_release(stage);
        std::cerr << "Native UsdGeom values did not round-trip.\n";
        return 66;
    }

    GfMatrix4d nativeTransform(0.0);
    for (int row = 0; row < 4; ++row)
    {
        for (int column = 0; column < 4; ++column)
        {
            nativeTransform[row][column] =
                readLocalTransform.values[(row * 4) + column];
        }
    }
    const GfVec3d transformedPoint = nativeTransform.Transform(GfVec3d(1.0, 2.0, 3.0));
    if (transformedPoint != GfVec3d(6.0, 8.0, 10.0))
    {
        openusd_stage_release(stage);
        std::cerr << "OpenUSD row-vector matrix semantics were not preserved.\n";
        return 66;
    }

    const int32_t malformedCounts[] = {3};
    const openusd_vec2f invalidClippingRange{10.0F, 1.0F};
    if (openusd_geom_mesh_set_topology(
            stage,
            "/World/Mesh",
            malformedCounts,
            1,
            geomIndices,
            4,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_geom_mesh_set_points(
            stage, "/World", geomPoints, 4, 0, 0, &error) !=
            OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_geom_mesh_set_normals(
            stage,
            "/World/Mesh",
            geomNormals,
            4,
            OPENUSD_GEOM_INTERPOLATION_CONSTANT,
            0,
            0,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_geom_mesh_set_normals(
            stage,
            "/World/Mesh",
            geomNormals,
            4,
            OPENUSD_GEOM_INTERPOLATION_UNIFORM,
            0,
            0,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_geom_mesh_set_normals(
            stage,
            "/World/Mesh",
            oneGeomNormal,
            1,
            OPENUSD_GEOM_INTERPOLATION_VARYING,
            0,
            0,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_geom_mesh_set_normals(
            stage,
            "/World/Mesh",
            oneGeomNormal,
            1,
            OPENUSD_GEOM_INTERPOLATION_FACE_VARYING,
            0,
            0,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_geom_mesh_set_normals(
            stage,
            "/World/Mesh",
            oneGeomNormal,
            1,
            OPENUSD_GEOM_INTERPOLATION_VERTEX,
            1,
            10.0,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_geom_camera_set_projection(
            stage, "/World/Camera", OPENUSD_GEOM_CAMERA_PERSPECTIVE, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_camera_set_float_property(
            stage,
            "/World/Camera",
            OPENUSD_GEOM_CAMERA_FOCAL_LENGTH,
            0.0F,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_geom_camera_set_projection(
            stage, "/World/Camera", OPENUSD_GEOM_CAMERA_ORTHOGRAPHIC, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_camera_set_float_property(
            stage,
            "/World/Camera",
            OPENUSD_GEOM_CAMERA_FOCAL_LENGTH,
            -1.0F,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_geom_camera_set_float_property(
            stage,
            "/World/Camera",
            OPENUSD_GEOM_CAMERA_HORIZONTAL_APERTURE,
            0.0F,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_geom_camera_set_clipping_range(
            stage, "/World/Camera", &invalidClippingRange, &error) !=
            OPENUSD_STATUS_INVALID_ARGUMENT)
    {
        openusd_stage_release(stage);
        std::cerr << "UsdGeom topology or schema validation failed.\n";
        return 67;
    }
    openusd_stage_release(stage);

    stage = nullptr;
    status = openusd_stage_open(usdGeomPath.string().c_str(), &stage, &error);
    readGeomPoints.clear();
    if (status != OPENUSD_STATUS_OK ||
        !ReadGeomArray(
            stage,
            "/World/Mesh",
            0,
            0,
            openusd_geom_mesh_get_points,
            &error,
            &readGeomPoints) ||
        readGeomPoints.size() != 4 ||
        readGeomPoints[0].x != -1.0F ||
        openusd_geom_camera_get_float_property(
            stage,
            "/World/Camera",
            OPENUSD_GEOM_CAMERA_HORIZONTAL_APERTURE,
            &cameraValue,
            &error) != OPENUSD_STATUS_OK ||
        cameraValue != 24.0F)
    {
        openusd_stage_release(stage);
        std::cerr << "Saved native UsdGeom data did not round-trip.\n";
        return 68;
    }
    openusd_stage_release(stage);
    std::cout << "UsdGeom facade passed.\n";

    const std::filesystem::path usdShadePath = directory / "native-usdshade-authored.usda";
    std::filesystem::remove(usdShadePath);
    stage = nullptr;
    status = openusd_stage_create_new(usdShadePath.string().c_str(), &stage, &error);
    if (status != OPENUSD_STATUS_OK ||
        openusd_geom_define_mesh(stage, "/World/Mesh", &error) != OPENUSD_STATUS_OK ||
        openusd_shade_define_material(stage, "/World/Looks/Material", &error) !=
            OPENUSD_STATUS_OK ||
        openusd_shade_define_shader(
            stage, "/World/Looks/Material/PreviewSurface", &error) != OPENUSD_STATUS_OK ||
        openusd_shade_shader_set_source_id(
            stage, "/World/Looks/Material/PreviewSurface", "UsdPreviewSurface", &error) !=
            OPENUSD_STATUS_OK ||
        openusd_shade_create_input(
            stage,
            "/World/Looks/Material/PreviewSurface",
            "roughness",
            OPENUSD_SHADE_VALUE_FLOAT,
            &error) != OPENUSD_STATUS_OK ||
        openusd_shade_set_input_float(
            stage, "/World/Looks/Material/PreviewSurface", "roughness", 0.65F, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_shade_create_input(
            stage,
            "/World/Looks/Material/PreviewSurface",
            "diffuseColor",
            OPENUSD_SHADE_VALUE_COLOR3F,
            &error) != OPENUSD_STATUS_OK ||
        openusd_shade_create_output(
            stage,
            "/World/Looks/Material/PreviewSurface",
            "surface",
            OPENUSD_SHADE_VALUE_TOKEN,
            &error) != OPENUSD_STATUS_OK ||
        openusd_shade_define_shader(stage, "/World/Looks/Material/Texture", &error) !=
            OPENUSD_STATUS_OK ||
        openusd_shade_shader_set_source_id(
            stage, "/World/Looks/Material/Texture", "UsdUVTexture", &error) !=
            OPENUSD_STATUS_OK ||
        openusd_shade_create_input(
            stage,
            "/World/Looks/Material/Texture",
            "file",
            OPENUSD_SHADE_VALUE_ASSET,
            &error) != OPENUSD_STATUS_OK ||
        openusd_shade_set_input_string(
            stage,
            "/World/Looks/Material/Texture",
            "file",
            OPENUSD_SHADE_VALUE_ASSET,
            "textures/albedo.png",
            &error) != OPENUSD_STATUS_OK ||
        openusd_shade_create_output(
            stage,
            "/World/Looks/Material/Texture",
            "rgb",
            OPENUSD_SHADE_VALUE_FLOAT3,
            &error) != OPENUSD_STATUS_OK ||
        openusd_shade_connect(
            stage,
            "/World/Looks/Material/PreviewSurface",
            "diffuseColor",
            OPENUSD_SHADE_ATTRIBUTE_INPUT,
            "/World/Looks/Material/Texture",
            "rgb",
            OPENUSD_SHADE_ATTRIBUTE_OUTPUT,
            &error) != OPENUSD_STATUS_OK ||
        openusd_shade_material_create_surface_output(
            stage, "/World/Looks/Material", &error) != OPENUSD_STATUS_OK ||
        openusd_shade_connect(
            stage,
            "/World/Looks/Material",
            "surface",
            OPENUSD_SHADE_ATTRIBUTE_OUTPUT,
            "/World/Looks/Material/PreviewSurface",
            "surface",
            OPENUSD_SHADE_ATTRIBUTE_OUTPUT,
            &error) != OPENUSD_STATUS_OK ||
        openusd_shade_material_bind(
            stage, "/World/Mesh", "/World/Looks/Material", &error) != OPENUSD_STATUS_OK)
    {
        openusd_stage_release(stage);
        std::cerr << "Could not author the native UsdShade network.\n";
        return 69;
    }

    float roughness = 0.0F;
    openusd_shade_value_type shadeValueType = OPENUSD_SHADE_VALUE_INVALID;
    if (openusd_shade_get_input_float(
            stage,
            "/World/Looks/Material/PreviewSurface",
            "roughness",
            &roughness,
            &error) != OPENUSD_STATUS_OK ||
        roughness != 0.65F ||
        openusd_shade_get_output_type(
            stage,
            "/World/Looks/Material/Texture",
            "rgb",
            &shadeValueType,
            &error) != OPENUSD_STATUS_OK ||
        shadeValueType != OPENUSD_SHADE_VALUE_FLOAT3 ||
        openusd_shade_create_input(
            stage,
            "/World/Looks/Material/PreviewSurface",
            "roughness",
            OPENUSD_SHADE_VALUE_TOKEN,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_shade_connect(
            stage,
            "/World/Looks/Material/PreviewSurface",
            "roughness",
            OPENUSD_SHADE_ATTRIBUTE_INPUT,
            "/World/Looks/Material/Texture",
            "rgb",
            OPENUSD_SHADE_ATTRIBUTE_OUTPUT,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_shade_connect(
            stage,
            "/World/Looks/Material/PreviewSurface",
            "diffuseColor",
            OPENUSD_SHADE_ATTRIBUTE_INPUT,
            "/World/Looks/Material/Texture",
            "missing",
            OPENUSD_SHADE_ATTRIBUTE_OUTPUT,
            &error) != OPENUSD_STATUS_NOT_FOUND ||
        openusd_stage_save(stage, &error) != OPENUSD_STATUS_OK)
    {
        openusd_stage_release(stage);
        std::cerr << "UsdShade values or mismatch validation failed.\n";
        return 70;
    }
    openusd_stage_release(stage);

    stage = nullptr;
    status = openusd_stage_open(usdShadePath.string().c_str(), &stage, &error);
    std::string previewSourceId;
    std::string directMaterial;
    if (status != OPENUSD_STATUS_OK ||
        !ReadPrimString(
            stage,
            "/World/Looks/Material/PreviewSurface",
            openusd_shade_shader_get_source_id,
            &error,
            &previewSourceId) ||
        previewSourceId != "UsdPreviewSurface" ||
        !ReadPrimString(
            stage,
            "/World/Mesh",
            openusd_shade_get_direct_material,
            &error,
            &directMaterial) ||
        directMaterial != "/World/Looks/Material")
    {
        openusd_stage_release(stage);
        std::cerr << "Saved native UsdShade schemas or binding did not round-trip.\n";
        return 71;
    }

    size_t assetRequired = 0;
    status = openusd_shade_get_input_string(
        stage,
        "/World/Looks/Material/Texture",
        "file",
        OPENUSD_SHADE_VALUE_ASSET,
        nullptr,
        0,
        &assetRequired,
        &error);
    std::vector<char> assetBuffer(assetRequired);
    openusd_string_list* sourceList = nullptr;
    openusd_string_list_view sourceView{
        static_cast<uint32_t>(sizeof(openusd_string_list_view)), nullptr, 0, nullptr, 0, 0};
    openusd_shade_attribute_type sourceType = OPENUSD_SHADE_ATTRIBUTE_INVALID;
    if (status != OPENUSD_STATUS_BUFFER_TOO_SMALL || assetRequired == 0 ||
        openusd_shade_get_input_string(
            stage,
            "/World/Looks/Material/Texture",
            "file",
            OPENUSD_SHADE_VALUE_ASSET,
            assetBuffer.data(),
            assetBuffer.size(),
            &assetRequired,
            &error) != OPENUSD_STATUS_OK ||
        std::string(assetBuffer.data()) != "textures/albedo.png" ||
        openusd_shade_get_connected_source(
            stage,
            "/World/Looks/Material/PreviewSurface",
            "diffuseColor",
            OPENUSD_SHADE_ATTRIBUTE_INPUT,
            &sourceList,
            &sourceView,
            &sourceType,
            &error) != OPENUSD_STATUS_OK ||
        sourceView.count != 2 ||
        std::string(sourceView.data + sourceView.offsets[0]) !=
            "/World/Looks/Material/Texture" ||
        std::string(sourceView.data + sourceView.offsets[1]) != "rgb" ||
        sourceType != OPENUSD_SHADE_ATTRIBUTE_OUTPUT)
    {
        openusd_string_list_release(sourceList);
        openusd_stage_release(stage);
        std::cerr << "Saved native UsdShade asset or connection did not round-trip.\n";
        return 72;
    }
    openusd_string_list_release(sourceList);
    openusd_stage_release(stage);
    std::cout << "UsdShade facade passed.\n";

    const std::filesystem::path usdLuxPath = directory / "native-usdlux-authored.usda";
    std::filesystem::remove(usdLuxPath);
    stage = nullptr;
    status = openusd_stage_create_new(usdLuxPath.string().c_str(), &stage, &error);
    const openusd_vec3f lightColor{1.0F, 0.8F, 0.6F};
    if (status != OPENUSD_STATUS_OK ||
        openusd_lux_define(
            stage, "/World/Lights/Sun", OPENUSD_LUX_SCHEMA_DISTANT_LIGHT, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_lux_define(
            stage, "/World/Lights/Bulb", OPENUSD_LUX_SCHEMA_SPHERE_LIGHT, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_lux_define(
            stage, "/World/Lights/Panel", OPENUSD_LUX_SCHEMA_RECT_LIGHT, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_lux_define(
            stage, "/World/Lights/Environment", OPENUSD_LUX_SCHEMA_DOME_LIGHT, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_lux_set_float(
            stage, "/World/Lights/Sun", OPENUSD_LUX_FLOAT_INTENSITY, 4.5F, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_lux_set_float(
            stage, "/World/Lights/Sun", OPENUSD_LUX_FLOAT_EXPOSURE, 2.0F, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_lux_set_color(stage, "/World/Lights/Sun", lightColor, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_lux_set_shape(
            stage, "/World/Lights/Sun", OPENUSD_LUX_SHAPE_ANGLE, 0.75F, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_lux_set_bool(
            stage, "/World/Lights/Bulb", OPENUSD_LUX_BOOL_NORMALIZE, 1, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_lux_set_shape(
            stage, "/World/Lights/Bulb", OPENUSD_LUX_SHAPE_RADIUS, 0.25F, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_lux_set_shape(
            stage, "/World/Lights/Panel", OPENUSD_LUX_SHAPE_WIDTH, 3.0F, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_lux_set_shape(
            stage, "/World/Lights/Panel", OPENUSD_LUX_SHAPE_HEIGHT, 2.0F, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_lux_set_asset(
            stage,
            "/World/Lights/Panel",
            OPENUSD_LUX_ASSET_TEXTURE_FILE,
            "textures/panel.exr",
            &error) != OPENUSD_STATUS_OK ||
        openusd_lux_set_asset(
            stage,
            "/World/Lights/Environment",
            OPENUSD_LUX_ASSET_TEXTURE_FILE,
            "textures/studio.hdr",
            &error) != OPENUSD_STATUS_OK)
    {
        openusd_stage_release(stage);
        std::cerr << "Could not author native UsdLux lights.\n";
        return 73;
    }

    int32_t isSchema = 0;
    int32_t hasShaping = 0;
    float lightValue = 0.0F;
    openusd_vec3f readLightColor{};
    if (openusd_lux_is_schema(
            stage,
            "/World/Lights/Sun",
            OPENUSD_LUX_SCHEMA_DISTANT_LIGHT,
            &isSchema,
            &error) != OPENUSD_STATUS_OK ||
        isSchema != 1 ||
        openusd_lux_is_schema(
            stage,
            "/World/Lights/Bulb",
            OPENUSD_LUX_SCHEMA_DISTANT_LIGHT,
            &isSchema,
            &error) != OPENUSD_STATUS_OK ||
        isSchema != 0 ||
        openusd_lux_get_float(
            stage, "/World/Lights/Sun", OPENUSD_LUX_FLOAT_INTENSITY, &lightValue, &error) !=
            OPENUSD_STATUS_OK ||
        lightValue != 4.5F ||
        openusd_lux_get_color(stage, "/World/Lights/Sun", &readLightColor, &error) !=
            OPENUSD_STATUS_OK ||
        readLightColor.x != lightColor.x ||
        readLightColor.y != lightColor.y ||
        readLightColor.z != lightColor.z ||
        openusd_lux_has_shaping(stage, "/World/Lights/Bulb", &hasShaping, &error) !=
            OPENUSD_STATUS_OK ||
        hasShaping != 0 ||
        openusd_lux_set_shaping(
            stage,
            "/World/Lights/Bulb",
            OPENUSD_LUX_SHAPING_CONE_ANGLE,
            35.0F,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_lux_apply_shaping(stage, "/World/Lights/Bulb", &error) !=
            OPENUSD_STATUS_OK ||
        openusd_lux_set_shaping(
            stage,
            "/World/Lights/Bulb",
            OPENUSD_LUX_SHAPING_FOCUS,
            2.5F,
            &error) != OPENUSD_STATUS_OK ||
        openusd_lux_set_shaping(
            stage,
            "/World/Lights/Bulb",
            OPENUSD_LUX_SHAPING_CONE_ANGLE,
            35.0F,
            &error) != OPENUSD_STATUS_OK ||
        openusd_lux_set_shaping(
            stage,
            "/World/Lights/Bulb",
            OPENUSD_LUX_SHAPING_CONE_SOFTNESS,
            0.2F,
            &error) != OPENUSD_STATUS_OK ||
        openusd_lux_set_shape(
            stage, "/World/Lights/Bulb", OPENUSD_LUX_SHAPE_WIDTH, 1.0F, &error) !=
            OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_lux_set_float(
            stage,
            "/World/Lights/Sun",
            OPENUSD_LUX_FLOAT_INTENSITY,
            std::numeric_limits<float>::quiet_NaN(),
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_lux_set_float(
            stage,
            "/World/Lights/Sun",
            OPENUSD_LUX_FLOAT_INTENSITY,
            -1.0F,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_lux_set_float(
            stage,
            "/World/Lights/Sun",
            OPENUSD_LUX_FLOAT_COLOR_TEMPERATURE,
            999.0F,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_lux_set_shape(
            stage,
            "/World/Lights/Sun",
            OPENUSD_LUX_SHAPE_ANGLE,
            360.0F,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_lux_set_shape(
            stage,
            "/World/Lights/Bulb",
            OPENUSD_LUX_SHAPE_RADIUS,
            -0.1F,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_lux_set_shaping(
            stage,
            "/World/Lights/Bulb",
            OPENUSD_LUX_SHAPING_FOCUS,
            -1.0F,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_lux_set_shaping(
            stage,
            "/World/Lights/Bulb",
            OPENUSD_LUX_SHAPING_CONE_ANGLE,
            181.0F,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_lux_set_shaping(
            stage,
            "/World/Lights/Bulb",
            OPENUSD_LUX_SHAPING_CONE_SOFTNESS,
            1.01F,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_lux_define(
            stage, "relative/light", OPENUSD_LUX_SCHEMA_SPHERE_LIGHT, &error) !=
            OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_lux_get_float(
            stage,
            "/World/Lights/Missing",
            OPENUSD_LUX_FLOAT_INTENSITY,
            &lightValue,
            &error) != OPENUSD_STATUS_NOT_FOUND ||
        openusd_stage_save(stage, &error) != OPENUSD_STATUS_OK)
    {
        openusd_stage_release(stage);
        std::cerr << "UsdLux values or validation failed.\n";
        return 74;
    }
    openusd_stage_release(stage);

    stage = nullptr;
    status = openusd_stage_open(usdLuxPath.string().c_str(), &stage, &error);
    size_t luxAssetRequired = 0;
    openusd_status luxAssetStatus = status == OPENUSD_STATUS_OK
        ? openusd_lux_get_asset(
              stage,
              "/World/Lights/Environment",
              OPENUSD_LUX_ASSET_TEXTURE_FILE,
              nullptr,
              0,
              &luxAssetRequired,
              &error)
        : status;
    std::vector<char> luxAssetBuffer(luxAssetRequired);
    if (status != OPENUSD_STATUS_OK ||
        openusd_lux_get_shape(
            stage, "/World/Lights/Sun", OPENUSD_LUX_SHAPE_ANGLE, &lightValue, &error) !=
            OPENUSD_STATUS_OK ||
        lightValue != 0.75F ||
        openusd_lux_get_shaping(
            stage,
            "/World/Lights/Bulb",
            OPENUSD_LUX_SHAPING_CONE_ANGLE,
            &lightValue,
            &error) != OPENUSD_STATUS_OK ||
        lightValue != 35.0F ||
        luxAssetStatus != OPENUSD_STATUS_BUFFER_TOO_SMALL ||
        luxAssetRequired == 0 ||
        openusd_lux_get_asset(
            stage,
            "/World/Lights/Environment",
            OPENUSD_LUX_ASSET_TEXTURE_FILE,
            luxAssetBuffer.data(),
            luxAssetBuffer.size(),
            &luxAssetRequired,
            &error) != OPENUSD_STATUS_OK ||
        std::string(luxAssetBuffer.data()) != "textures/studio.hdr")
    {
        openusd_stage_release(stage);
        std::cerr << "Saved native UsdLux lights did not round-trip.\n";
        return 75;
    }
    openusd_stage_release(stage);
    std::cout << "UsdLux facade passed.\n";

    const std::filesystem::path usdSkelPath = directory / "native-usdskel-authored.usda";
    std::filesystem::remove(usdSkelPath);
    stage = nullptr;
    status = openusd_stage_create_new(usdSkelPath.string().c_str(), &stage, &error);

    std::array<char, 14> skelJointData{
        'R', 'o', 'o', 't', '\0',
        'R', 'o', 'o', 't', '/', 'A', 'r', 'm', '\0'};
    std::array<size_t, 2> skelJointOffsets{0, 5};
    openusd_string_list_view skelJointView{
        static_cast<uint32_t>(sizeof(openusd_string_list_view)),
        skelJointData.data(),
        skelJointData.size(),
        skelJointOffsets.data(),
        skelJointOffsets.size() * sizeof(size_t),
        skelJointOffsets.size()};

    openusd_matrix4d identity{};
    identity.values[0] = 1.0;
    identity.values[5] = 1.0;
    identity.values[10] = 1.0;
    identity.values[15] = 1.0;
    openusd_matrix4d armTransform = identity;
    armTransform.values[13] = 1.0;
    const std::array<openusd_matrix4d, 2> skelTransforms{identity, armTransform};
    const std::array<openusd_vec3f, 2> skelTranslations{
        openusd_vec3f{0.0F, 0.0F, 0.0F},
        openusd_vec3f{0.0F, 1.0F, 0.0F}};
    const std::array<openusd_vec3f, 2> sampledSkelTranslations{
        openusd_vec3f{0.0F, 0.0F, 0.0F},
        openusd_vec3f{0.0F, 2.0F, 0.0F}};
    const std::array<openusd_vec3f, 2> skelScales{
        openusd_vec3f{1.0F, 1.0F, 1.0F},
        openusd_vec3f{1.0F, 1.0F, 1.0F}};
    const std::array<openusd_quatf, 2> skelRotations{
        openusd_quatf{1.0F, 0.0F, 0.0F, 0.0F},
        openusd_quatf{1.0F, 0.0F, 0.0F, 0.0F}};
    const std::array<openusd_quatf, 2> sampledSkelRotations{
        openusd_quatf{1.0F, 0.0F, 0.0F, 0.0F},
        openusd_quatf{0.70710677F, 0.0F, 0.0F, 0.70710677F}};
    const std::array<int32_t, 6> skelJointIndices{0, 1, 0, 1, 0, 1};
    const std::array<float, 6> skelJointWeights{1.0F, 0.0F, 0.5F, 0.5F, 0.0F, 1.0F};

    if (status != OPENUSD_STATUS_OK ||
        openusd_skel_define(
            stage, "/World/Character", OPENUSD_SKEL_SCHEMA_ROOT, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_skel_define(
            stage, "/World/Character/Skeleton", OPENUSD_SKEL_SCHEMA_SKELETON, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_skel_define(
            stage, "/World/Character/Animation", OPENUSD_SKEL_SCHEMA_ANIMATION, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_geom_define_mesh(stage, "/World/Character/Mesh", &error) != OPENUSD_STATUS_OK ||
        openusd_skel_set_joints(
            stage,
            "/World/Character/Skeleton",
            OPENUSD_SKEL_SCHEMA_SKELETON,
            &skelJointView,
            &error) != OPENUSD_STATUS_OK ||
        openusd_skel_set_skeleton_matrices(
            stage,
            "/World/Character/Skeleton",
            OPENUSD_SKEL_MATRIX_BIND_TRANSFORMS,
            skelTransforms.data(),
            skelTransforms.size(),
            &error) != OPENUSD_STATUS_OK ||
        openusd_skel_set_skeleton_matrices(
            stage,
            "/World/Character/Skeleton",
            OPENUSD_SKEL_MATRIX_REST_TRANSFORMS,
            skelTransforms.data(),
            skelTransforms.size(),
            &error) != OPENUSD_STATUS_OK ||
        openusd_skel_set_joints(
            stage,
            "/World/Character/Animation",
            OPENUSD_SKEL_SCHEMA_ANIMATION,
            &skelJointView,
            &error) != OPENUSD_STATUS_OK ||
        openusd_skel_set_animation_vec3(
            stage,
            "/World/Character/Animation",
            OPENUSD_SKEL_ANIMATION_TRANSLATIONS,
            skelTranslations.data(),
            skelTranslations.size(),
            0,
            0.0,
            &error) != OPENUSD_STATUS_OK ||
        openusd_skel_set_animation_vec3(
            stage,
            "/World/Character/Animation",
            OPENUSD_SKEL_ANIMATION_TRANSLATIONS,
            sampledSkelTranslations.data(),
            sampledSkelTranslations.size(),
            1,
            10.0,
            &error) != OPENUSD_STATUS_OK ||
        openusd_skel_set_animation_rotations(
            stage,
            "/World/Character/Animation",
            skelRotations.data(),
            skelRotations.size(),
            0,
            0.0,
            &error) != OPENUSD_STATUS_OK ||
        openusd_skel_set_animation_rotations(
            stage,
            "/World/Character/Animation",
            sampledSkelRotations.data(),
            sampledSkelRotations.size(),
            1,
            10.0,
            &error) != OPENUSD_STATUS_OK ||
        openusd_skel_set_animation_vec3(
            stage,
            "/World/Character/Animation",
            OPENUSD_SKEL_ANIMATION_SCALES,
            skelScales.data(),
            skelScales.size(),
            0,
            0.0,
            &error) != OPENUSD_STATUS_OK ||
        openusd_skel_apply_binding(stage, "/World/Character", &error) != OPENUSD_STATUS_OK ||
        openusd_skel_set_binding_target(
            stage,
            "/World/Character",
            OPENUSD_SKEL_BINDING_SKELETON,
            "/World/Character/Skeleton",
            &error) != OPENUSD_STATUS_OK ||
        openusd_skel_apply_binding(stage, "/World/Character/Skeleton", &error) !=
            OPENUSD_STATUS_OK ||
        openusd_skel_set_binding_target(
            stage,
            "/World/Character/Skeleton",
            OPENUSD_SKEL_BINDING_ANIMATION_SOURCE,
            "/World/Character/Animation",
            &error) != OPENUSD_STATUS_OK ||
        openusd_skel_apply_binding(stage, "/World/Character/Mesh", &error) != OPENUSD_STATUS_OK ||
        openusd_skel_set_geom_bind_transform(
            stage, "/World/Character/Mesh", &identity, &error) != OPENUSD_STATUS_OK ||
        openusd_skel_set_joint_influences(
            stage,
            "/World/Character/Mesh",
            skelJointIndices.data(),
            skelJointIndices.size(),
            skelJointWeights.data(),
            skelJointWeights.size(),
            2,
            OPENUSD_SKEL_INTERPOLATION_VERTEX,
            &error) != OPENUSD_STATUS_OK)
    {
        openusd_stage_release(stage);
        std::cerr << "Could not author native UsdSkel data: " << errorText.data() << '\n';
        return 76;
    }

    std::array<char, 14> invalidSkelJointData{
        'R', 'o', 'o', 't', '/', 'A', 'r', 'm', '\0',
        'R', 'o', 'o', 't', '\0'};
    std::array<size_t, 2> invalidSkelJointOffsets{0, 9};
    openusd_string_list_view invalidSkelJointView{
        static_cast<uint32_t>(sizeof(openusd_string_list_view)),
        invalidSkelJointData.data(),
        invalidSkelJointData.size(),
        invalidSkelJointOffsets.data(),
        invalidSkelJointOffsets.size() * sizeof(size_t),
        invalidSkelJointOffsets.size()};
    const std::array<openusd_vec3f, 1> invalidTranslations{
        openusd_vec3f{0.0F, 0.0F, 0.0F}};
    const std::array<openusd_quatf, 2> invalidRotations{
        openusd_quatf{1.0F, 0.0F, 0.0F, 0.0F},
        openusd_quatf{2.0F, 0.0F, 0.0F, 0.0F}};
    const std::array<int32_t, 2> invalidJointIndices{0, 2};
    const std::array<float, 2> invalidJointWeights{0.5F, 0.5F};
    int32_t skelIsSchema = 0;
    if (openusd_skel_is_schema(
            stage,
            "/World/Character/Skeleton",
            OPENUSD_SKEL_SCHEMA_SKELETON,
            &skelIsSchema,
            &error) != OPENUSD_STATUS_OK ||
        skelIsSchema != 1 ||
        openusd_skel_is_schema(
            stage,
            "/World/Character/Animation",
            OPENUSD_SKEL_SCHEMA_SKELETON,
            &skelIsSchema,
            &error) != OPENUSD_STATUS_OK ||
        skelIsSchema != 0 ||
        openusd_skel_set_joints(
            stage,
            "/World/Character/Skeleton",
            OPENUSD_SKEL_SCHEMA_SKELETON,
            &invalidSkelJointView,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_skel_set_animation_vec3(
            stage,
            "/World/Character/Animation",
            OPENUSD_SKEL_ANIMATION_TRANSLATIONS,
            invalidTranslations.data(),
            invalidTranslations.size(),
            1,
            20.0,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_skel_set_animation_rotations(
            stage,
            "/World/Character/Animation",
            invalidRotations.data(),
            invalidRotations.size(),
            1,
            20.0,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_skel_set_binding_target(
            stage,
            "/World/Character",
            OPENUSD_SKEL_BINDING_SKELETON,
            "/World/Character/Missing",
            &error) != OPENUSD_STATUS_NOT_FOUND ||
        openusd_skel_set_joint_influences(
            stage,
            "/World/Character/Mesh",
            skelJointIndices.data(),
            2,
            skelJointWeights.data(),
            1,
            2,
            OPENUSD_SKEL_INTERPOLATION_VERTEX,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_skel_set_joint_influences(
            stage,
            "/World/Character/Mesh",
            invalidJointIndices.data(),
            invalidJointIndices.size(),
            invalidJointWeights.data(),
            invalidJointWeights.size(),
            2,
            OPENUSD_SKEL_INTERPOLATION_CONSTANT,
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_stage_save(stage, &error) != OPENUSD_STATUS_OK)
    {
        openusd_stage_release(stage);
        std::cerr << "Native UsdSkel validation failed: " << errorText.data() << '\n';
        return 77;
    }
    openusd_stage_release(stage);

    stage = nullptr;
    status = openusd_stage_open(usdSkelPath.string().c_str(), &stage, &error);
    openusd_string_list* readSkelJointsList = nullptr;
    openusd_string_list_view readSkelJointsView{
        static_cast<uint32_t>(sizeof(openusd_string_list_view)), nullptr, 0, nullptr, 0, 0};
    size_t skelMatrixRequired = 0;
    size_t skelTranslationRequired = 0;
    size_t skelRotationRequired = 0;
    size_t skelIndexRequired = 0;
    size_t skelWeightRequired = 0;
    int32_t skelElementSize = 0;
    openusd_skel_interpolation skelInterpolation = OPENUSD_SKEL_INTERPOLATION_CONSTANT;
    size_t skeletonTargetRequired = 0;
    size_t animationTargetRequired = 0;
    openusd_matrix4d readGeomBindTransform{};
    const openusd_status skelMatrixStatus =
        status == OPENUSD_STATUS_OK
        ? openusd_skel_get_skeleton_matrices(
              stage,
              "/World/Character/Skeleton",
              OPENUSD_SKEL_MATRIX_BIND_TRANSFORMS,
              nullptr,
              0,
              &skelMatrixRequired,
              &error)
        : status;
    const openusd_status skelTranslationStatus =
        status == OPENUSD_STATUS_OK
        ? openusd_skel_get_animation_vec3(
              stage,
              "/World/Character/Animation",
              OPENUSD_SKEL_ANIMATION_TRANSLATIONS,
              1,
              10.0,
              nullptr,
              0,
              &skelTranslationRequired,
              &error)
        : status;
    const openusd_status skelRotationStatus =
        status == OPENUSD_STATUS_OK
        ? openusd_skel_get_animation_rotations(
              stage,
              "/World/Character/Animation",
              1,
              10.0,
              nullptr,
              0,
              &skelRotationRequired,
              &error)
        : status;
    const openusd_status skelInfluenceStatus =
        status == OPENUSD_STATUS_OK
        ? openusd_skel_get_joint_influences(
              stage,
              "/World/Character/Mesh",
              nullptr,
              0,
              &skelIndexRequired,
              nullptr,
              0,
              &skelWeightRequired,
              &skelElementSize,
              &skelInterpolation,
              &error)
        : status;
    const openusd_status skeletonTargetStatus =
        status == OPENUSD_STATUS_OK
        ? openusd_skel_get_binding_target(
              stage,
              "/World/Character",
              OPENUSD_SKEL_BINDING_SKELETON,
              nullptr,
              0,
              &skeletonTargetRequired,
              &error)
        : status;
    const openusd_status animationTargetStatus =
        status == OPENUSD_STATUS_OK
        ? openusd_skel_get_binding_target(
              stage,
              "/World/Character/Skeleton",
              OPENUSD_SKEL_BINDING_ANIMATION_SOURCE,
              nullptr,
              0,
              &animationTargetRequired,
              &error)
        : status;

    std::vector<openusd_matrix4d> readSkelMatrices(skelMatrixRequired);
    std::vector<openusd_vec3f> readSkelTranslations(skelTranslationRequired);
    std::vector<openusd_quatf> readSkelRotations(skelRotationRequired);
    std::vector<int32_t> readSkelIndices(skelIndexRequired);
    std::vector<float> readSkelWeights(skelWeightRequired);
    std::vector<char> skeletonTarget(skeletonTargetRequired);
    std::vector<char> animationTarget(animationTargetRequired);
    if (status != OPENUSD_STATUS_OK ||
        openusd_skel_get_joints(
            stage,
            "/World/Character/Skeleton",
            OPENUSD_SKEL_SCHEMA_SKELETON,
            &readSkelJointsList,
            &readSkelJointsView,
            &error) != OPENUSD_STATUS_OK ||
        readSkelJointsView.count != 2 ||
        std::string(readSkelJointsView.data + readSkelJointsView.offsets[0]) != "Root" ||
        std::string(readSkelJointsView.data + readSkelJointsView.offsets[1]) != "Root/Arm" ||
        skelMatrixStatus != OPENUSD_STATUS_OK ||
        skelMatrixRequired != 2 ||
        openusd_skel_get_skeleton_matrices(
            stage,
            "/World/Character/Skeleton",
            OPENUSD_SKEL_MATRIX_BIND_TRANSFORMS,
            readSkelMatrices.data(),
            readSkelMatrices.size(),
            &skelMatrixRequired,
            &error) != OPENUSD_STATUS_OK ||
        readSkelMatrices[1].values[13] != 1.0 ||
        skelTranslationStatus != OPENUSD_STATUS_OK ||
        skelTranslationRequired != 2 ||
        openusd_skel_get_animation_vec3(
            stage,
            "/World/Character/Animation",
            OPENUSD_SKEL_ANIMATION_TRANSLATIONS,
            1,
            10.0,
            readSkelTranslations.data(),
            readSkelTranslations.size(),
            &skelTranslationRequired,
            &error) != OPENUSD_STATUS_OK ||
        readSkelTranslations[1].y != 2.0F ||
        skelRotationStatus != OPENUSD_STATUS_OK ||
        skelRotationRequired != 2 ||
        openusd_skel_get_animation_rotations(
            stage,
            "/World/Character/Animation",
            1,
            10.0,
            readSkelRotations.data(),
            readSkelRotations.size(),
            &skelRotationRequired,
            &error) != OPENUSD_STATUS_OK ||
        readSkelRotations[1].real != sampledSkelRotations[1].real ||
        readSkelRotations[1].z != sampledSkelRotations[1].z ||
        skelInfluenceStatus != OPENUSD_STATUS_OK ||
        skelIndexRequired != skelJointIndices.size() ||
        skelWeightRequired != skelJointWeights.size() ||
        openusd_skel_get_joint_influences(
            stage,
            "/World/Character/Mesh",
            readSkelIndices.data(),
            readSkelIndices.size(),
            &skelIndexRequired,
            readSkelWeights.data(),
            readSkelWeights.size(),
            &skelWeightRequired,
            &skelElementSize,
            &skelInterpolation,
            &error) != OPENUSD_STATUS_OK ||
        readSkelIndices !=
            std::vector<int32_t>(skelJointIndices.begin(), skelJointIndices.end()) ||
        readSkelWeights !=
            std::vector<float>(skelJointWeights.begin(), skelJointWeights.end()) ||
        skelElementSize != 2 ||
        skelInterpolation != OPENUSD_SKEL_INTERPOLATION_VERTEX ||
        skeletonTargetStatus != OPENUSD_STATUS_BUFFER_TOO_SMALL ||
        animationTargetStatus != OPENUSD_STATUS_BUFFER_TOO_SMALL ||
        openusd_skel_get_binding_target(
            stage,
            "/World/Character",
            OPENUSD_SKEL_BINDING_SKELETON,
            skeletonTarget.data(),
            skeletonTarget.size(),
            &skeletonTargetRequired,
            &error) != OPENUSD_STATUS_OK ||
        std::string(skeletonTarget.data()) != "/World/Character/Skeleton" ||
        openusd_skel_get_binding_target(
            stage,
            "/World/Character/Skeleton",
            OPENUSD_SKEL_BINDING_ANIMATION_SOURCE,
            animationTarget.data(),
            animationTarget.size(),
            &animationTargetRequired,
            &error) != OPENUSD_STATUS_OK ||
        std::string(animationTarget.data()) != "/World/Character/Animation" ||
        openusd_skel_get_geom_bind_transform(
            stage, "/World/Character/Mesh", &readGeomBindTransform, &error) !=
            OPENUSD_STATUS_OK ||
        readGeomBindTransform.values[0] != 1.0 ||
        readGeomBindTransform.values[15] != 1.0)
    {
        openusd_string_list_release(readSkelJointsList);
        openusd_stage_release(stage);
        std::cerr << "Saved native UsdSkel data did not round-trip: " << errorText.data() << '\n';
        return 78;
    }
    openusd_string_list_release(readSkelJointsList);
    openusd_stage_release(stage);
    std::cout << "UsdSkel facade passed.\n";

    const std::filesystem::path editSublayerPath = directory / "native-edit-sublayer.usda";
    const std::filesystem::path editStagePath = directory / "native-edit-targets.usda";
    const std::filesystem::path foreignStagePath = directory / "native-foreign-edit-target.usda";
    std::filesystem::remove(editSublayerPath);
    std::filesystem::remove(editStagePath);
    std::filesystem::remove(foreignStagePath);

    stage = nullptr;
    status = openusd_stage_create_new(editSublayerPath.string().c_str(), &stage, &error);
    if (status != OPENUSD_STATUS_OK ||
        openusd_stage_define_prim(stage, "/World/FromSublayer", "Xform", &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_save(stage, &error) != OPENUSD_STATUS_OK)
    {
        openusd_stage_release(stage);
        std::cerr << "Could not create the edit-target sublayer.\n";
        return 33;
    }
    openusd_stage_release(stage);

    stage = nullptr;
    openusd_layer* editRootLayer = nullptr;
    openusd_layer* editSessionLayer = nullptr;
    status = openusd_stage_create_new(editStagePath.string().c_str(), &stage, &error);
    if (status != OPENUSD_STATUS_OK ||
        openusd_stage_get_root_layer(stage, &editRootLayer, &error) != OPENUSD_STATUS_OK ||
        openusd_stage_get_session_layer(stage, &editSessionLayer, &error) != OPENUSD_STATUS_OK ||
        openusd_layer_add_sublayer(editRootLayer, editSublayerPath.string().c_str(), &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_define_prim(stage, "/World/RootDirect", "Xform", &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_edit_target_session_layer(stage, &error) != OPENUSD_STATUS_OK ||
        openusd_stage_define_prim(stage, "/World/SessionDirect", "Xform", &error) !=
            OPENUSD_STATUS_OK)
    {
        openusd_layer_release(editSessionLayer);
        openusd_layer_release(editRootLayer);
        openusd_stage_release(stage);
        std::cerr << "Could not author root and session edit-target data.\n";
        return 34;
    }

    std::string editTargetIdentifier;
    std::string editRootIdentifier;
    std::string editSessionIdentifier;
    if (!ReadStageString(
            stage,
            openusd_stage_get_edit_target_layer_identifier,
            &error,
            &editTargetIdentifier) ||
        !ReadStageString(
            stage, openusd_stage_get_root_layer_identifier, &error, &editRootIdentifier) ||
        !ReadStageString(
            stage, openusd_stage_get_session_layer_identifier, &error, &editSessionIdentifier) ||
        editTargetIdentifier != editSessionIdentifier ||
        openusd_stage_set_edit_target_root_layer(stage, &error) != OPENUSD_STATUS_OK ||
        openusd_stage_define_prim(stage, "/World/RootConvenience", "Xform", &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_edit_target_layer(stage, editSessionLayer, &error) != OPENUSD_STATUS_OK ||
        openusd_stage_define_prim(stage, "/World/SessionOwned", "Xform", &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_set_edit_target_layer(stage, editRootLayer, &error) != OPENUSD_STATUS_OK ||
        !ReadStageString(
            stage,
            openusd_stage_get_edit_target_layer_identifier,
            &error,
            &editTargetIdentifier) ||
        editTargetIdentifier != editRootIdentifier)
    {
        openusd_layer_release(editSessionLayer);
        openusd_layer_release(editRootLayer);
        openusd_stage_release(stage);
        std::cerr << "Edit-target selection did not round-trip.\n";
        return 35;
    }

    openusd_stage* foreignStage = nullptr;
    openusd_layer* foreignLayer = nullptr;
    if (openusd_stage_create_new(foreignStagePath.string().c_str(), &foreignStage, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_get_root_layer(foreignStage, &foreignLayer, &error) != OPENUSD_STATUS_OK ||
        openusd_stage_set_edit_target_layer(stage, foreignLayer, &error) !=
            OPENUSD_STATUS_INVALID_ARGUMENT)
    {
        openusd_layer_release(foreignLayer);
        openusd_stage_release(foreignStage);
        openusd_layer_release(editSessionLayer);
        openusd_layer_release(editRootLayer);
        openusd_stage_release(stage);
        std::cerr << "A foreign layer handle was accepted as an edit target.\n";
        return 36;
    }
    openusd_layer_release(foreignLayer);
    openusd_stage_release(foreignStage);

    std::vector<std::string> layerStack;
    int32_t muted = 0;
    if (!ReadStageStringList(
            stage,
            openusd_stage_get_layer_stack_identifiers,
            &error,
            &layerStack) ||
        std::find(layerStack.begin(), layerStack.end(), editRootIdentifier) == layerStack.end() ||
        std::find(layerStack.begin(), layerStack.end(), editSessionIdentifier) == layerStack.end())
    {
        openusd_layer_release(editSessionLayer);
        openusd_layer_release(editRootLayer);
        openusd_stage_release(stage);
        std::cerr << "Layer-stack enumeration failed.\n";
        return 37;
    }

    const auto sublayerEntry = std::find_if(
        layerStack.begin(),
        layerStack.end(),
        [&](const std::string& identifier)
        {
            return identifier != editRootIdentifier && identifier != editSessionIdentifier;
        });
    if (sublayerEntry == layerStack.end())
    {
        openusd_layer_release(editSessionLayer);
        openusd_layer_release(editRootLayer);
        openusd_stage_release(stage);
        std::cerr << "The authored sublayer was not present in the layer stack.\n";
        return 37;
    }
    const std::string sublayerIdentifier = *sublayerEntry;

    if (
        openusd_stage_is_layer_muted(
            stage, sublayerIdentifier.c_str(), &muted, &error) != OPENUSD_STATUS_OK ||
        muted != 0 ||
        openusd_stage_mute_layer(stage, sublayerIdentifier.c_str(), &error) != OPENUSD_STATUS_OK ||
        openusd_stage_is_layer_muted(
            stage, sublayerIdentifier.c_str(), &muted, &error) != OPENUSD_STATUS_OK ||
        muted == 0 ||
        !ReadStageStringList(
            stage,
            openusd_stage_get_layer_stack_identifiers,
            &error,
            &layerStack) ||
        std::find(layerStack.begin(), layerStack.end(), sublayerIdentifier) != layerStack.end() ||
        openusd_stage_unmute_layer(stage, sublayerIdentifier.c_str(), &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_is_layer_muted(
            stage, sublayerIdentifier.c_str(), &muted, &error) != OPENUSD_STATUS_OK ||
        muted != 0 ||
        openusd_stage_mute_layer(stage, "missing-layer.usda", &error) !=
            OPENUSD_STATUS_NOT_FOUND ||
        openusd_stage_is_layer_muted(
            stage, "missing-layer.usda", &muted, &error) != OPENUSD_STATUS_NOT_FOUND ||
        openusd_stage_save(stage, &error) != OPENUSD_STATUS_OK)
    {
        openusd_layer_release(editSessionLayer);
        openusd_layer_release(editRootLayer);
        openusd_stage_release(stage);
        std::cerr << "Layer muting or missing-identifier validation failed.\n";
        return 37;
    }

    openusd_layer_release(editSessionLayer);
    openusd_layer_release(editRootLayer);
    openusd_stage_release(stage);

    stage = nullptr;
    int32_t rootDirectExists = 0;
    int32_t rootConvenienceExists = 0;
    int32_t sessionDirectExists = 0;
    int32_t sessionOwnedExists = 0;
    int32_t sublayerExists = 0;
    if (openusd_stage_open(editStagePath.string().c_str(), &stage, &error) != OPENUSD_STATUS_OK ||
        openusd_stage_has_prim(stage, "/World/RootDirect", &rootDirectExists, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_has_prim(stage, "/World/RootConvenience", &rootConvenienceExists, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_has_prim(stage, "/World/SessionDirect", &sessionDirectExists, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_has_prim(stage, "/World/SessionOwned", &sessionOwnedExists, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_has_prim(stage, "/World/FromSublayer", &sublayerExists, &error) !=
            OPENUSD_STATUS_OK ||
        rootDirectExists == 0 ||
        rootConvenienceExists == 0 ||
        sessionDirectExists != 0 ||
        sessionOwnedExists != 0 ||
        sublayerExists == 0)
    {
        openusd_stage_release(stage);
        std::cerr << "Root and session edit-target authorship did not persist correctly.\n";
        return 38;
    }
    openusd_stage_release(stage);
    std::cout << "Edit targets and layer controls passed.\n";

    const std::filesystem::path compositionSourcePath =
        directory / "native-composition-source.usda";
    const std::filesystem::path compositionControlsPath =
        directory / "native-composition-controls.usda";
    std::filesystem::remove(compositionSourcePath);
    std::filesystem::remove(compositionControlsPath);

    stage = nullptr;
    if (openusd_stage_create_new(compositionSourcePath.string().c_str(), &stage, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_define_prim(stage, "/Model", "Xform", &error) != OPENUSD_STATUS_OK ||
        openusd_stage_define_prim(stage, "/Model/Child", "Xform", &error) != OPENUSD_STATUS_OK ||
        openusd_stage_set_double(
            stage, "/Model", "custom:sourceValue", 33.0, 0, 0, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_save(stage, &error) != OPENUSD_STATUS_OK)
    {
        openusd_stage_release(stage);
        std::cerr << "Could not create the composition source stage.\n";
        return 39;
    }
    openusd_stage_release(stage);

    stage = nullptr;
    if (openusd_stage_create_new(compositionControlsPath.string().c_str(), &stage, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_create_class_prim(stage, "/InheritBase", &error) != OPENUSD_STATUS_OK ||
        openusd_stage_set_double(
            stage, "/InheritBase", "custom:inherited", 11.0, 0, 0, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_create_class_prim(stage, "/SpecializeBase", &error) != OPENUSD_STATUS_OK ||
        openusd_stage_set_double(
            stage, "/SpecializeBase", "custom:specialized", 22.0, 0, 0, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_define_prim(stage, "/World/Inherited", "Xform", &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_add_inherit(stage, "/World/Inherited", "/InheritBase", &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_define_prim(stage, "/World/Specialized", "Xform", &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_add_specialize(stage, "/World/Specialized", "/SpecializeBase", &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_define_prim(stage, "/World/Payload", "Xform", &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_add_payload(
            stage,
            "/World/Payload",
            compositionSourcePath.string().c_str(),
            "/Model",
            &error) != OPENUSD_STATUS_OK ||
        openusd_stage_define_prim(stage, "/World/Instance", "Xform", &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_add_reference(
            stage,
            "/World/Instance",
            compositionSourcePath.string().c_str(),
            "/Model",
            &error) != OPENUSD_STATUS_OK ||
        openusd_stage_set_instanceable(stage, "/World/Instance", 1, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_define_prim(stage, "/World/Keep", "Xform", &error) != OPENUSD_STATUS_OK ||
        openusd_stage_define_prim(stage, "/World/Keep/Child", "Xform", &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_define_prim(stage, "/World/Exclude", "Xform", &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_define_prim(stage, "/Other", "Xform", &error) != OPENUSD_STATUS_OK ||
        openusd_stage_save(stage, &error) != OPENUSD_STATUS_OK)
    {
        openusd_stage_release(stage);
        std::cerr << "Could not author composition controls.\n";
        return 40;
    }
    openusd_stage_release(stage);

    stage = nullptr;
    double inheritedValue = 0;
    double specializedValue = 0;
    int32_t loaded = 0;
    int32_t instance = 0;
    int32_t prototype = 0;
    std::string prototypePath;
    if (openusd_stage_open(compositionControlsPath.string().c_str(), &stage, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_get_double(
            stage, "/World/Inherited", "custom:inherited", 0, 0, &inheritedValue, &error) !=
            OPENUSD_STATUS_OK ||
        inheritedValue != 11.0 ||
        openusd_stage_get_double(
            stage, "/World/Specialized", "custom:specialized", 0, 0, &specializedValue, &error) !=
            OPENUSD_STATUS_OK ||
        specializedValue != 22.0 ||
        openusd_stage_is_prim_loaded(stage, "/World/Payload", &loaded, &error) !=
            OPENUSD_STATUS_OK ||
        loaded == 0 ||
        openusd_stage_unload_prim(stage, "/World/Payload", &error) != OPENUSD_STATUS_OK ||
        openusd_stage_is_prim_loaded(stage, "/World/Payload", &loaded, &error) !=
            OPENUSD_STATUS_OK ||
        loaded != 0 ||
        openusd_stage_load_prim(stage, "/World/Payload", &error) != OPENUSD_STATUS_OK ||
        openusd_stage_is_prim_loaded(stage, "/World/Payload", &loaded, &error) !=
            OPENUSD_STATUS_OK ||
        loaded == 0 ||
        openusd_stage_is_prim_instance(stage, "/World/Instance", &instance, &error) !=
            OPENUSD_STATUS_OK ||
        instance == 0 ||
        !ReadPrimString(
            stage,
            "/World/Instance",
            openusd_stage_get_prim_prototype_path,
            &error,
            &prototypePath) ||
        openusd_stage_is_prim_prototype(
            stage, prototypePath.c_str(), &prototype, &error) != OPENUSD_STATUS_OK ||
        prototype == 0)
    {
        openusd_stage_release(stage);
        std::cerr << "Composition, load, or prototype inspection failed.\n";
        return 41;
    }

    if (openusd_stage_add_inherit(stage, "/World/Inherited", "relative", &error) !=
            OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_stage_add_specialize(stage, "/World/Specialized", "/Missing", &error) !=
            OPENUSD_STATUS_NOT_FOUND ||
        openusd_stage_is_prim_instance(stage, "/Missing", &instance, &error) !=
            OPENUSD_STATUS_NOT_FOUND ||
        openusd_stage_get_prim_prototype_path(
            stage, "/World/Keep", nullptr, 0, &required, &error) !=
            OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_stage_load_prim(stage, prototypePath.c_str(), &error) !=
            OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_stage_clear_inherits(stage, "/World/Inherited", &error) != OPENUSD_STATUS_OK ||
        openusd_stage_clear_specializes(stage, "/World/Specialized", &error) !=
            OPENUSD_STATUS_OK ||
        openusd_stage_get_double(
            stage, "/World/Inherited", "custom:inherited", 0, 0, &inheritedValue, &error) !=
            OPENUSD_STATUS_NOT_FOUND ||
        openusd_stage_get_double(
            stage, "/World/Specialized", "custom:specialized", 0, 0, &specializedValue, &error) !=
            OPENUSD_STATUS_NOT_FOUND)
    {
        openusd_stage_release(stage);
        std::cerr << "Composition path, missing-prim, or prototype errors failed.\n";
        return 42;
    }
    openusd_stage_release(stage);

    std::array<char, 12> maskData{
        '/', 'W', 'o', 'r', 'l', 'd', '/', 'K', 'e', 'e', 'p', '\0'};
    const size_t maskOffset = 0;
    openusd_string_list_view maskView{
        static_cast<uint32_t>(sizeof(openusd_string_list_view)),
        maskData.data(),
        maskData.size(),
        &maskOffset,
        sizeof(maskOffset),
        1};
    stage = nullptr;
    if (openusd_stage_open_masked(
            compositionControlsPath.string().c_str(), &maskView, &stage, &error) !=
        OPENUSD_STATUS_OK)
    {
        std::cerr << "Could not open the masked stage.\n";
        return 43;
    }

    std::vector<std::string> maskedPaths;
    if (!ReadStageStringList(stage, openusd_stage_get_prim_paths, &error, &maskedPaths) ||
        std::find(maskedPaths.begin(), maskedPaths.end(), "/World") == maskedPaths.end() ||
        std::find(maskedPaths.begin(), maskedPaths.end(), "/World/Keep") == maskedPaths.end() ||
        std::find(maskedPaths.begin(), maskedPaths.end(), "/World/Keep/Child") ==
            maskedPaths.end() ||
        std::find(maskedPaths.begin(), maskedPaths.end(), "/World/Exclude") !=
            maskedPaths.end() ||
        std::find(maskedPaths.begin(), maskedPaths.end(), "/Other") != maskedPaths.end())
    {
        openusd_stage_release(stage);
        std::cerr << "Masked traversal did not exclude unrelated prims.\n";
        return 44;
    }
    openusd_stage_release(stage);

    std::array<char, 9> invalidMaskData{
        'r', 'e', 'l', 'a', 't', 'i', 'v', 'e', '\0'};
    openusd_string_list_view invalidMaskView{
        static_cast<uint32_t>(sizeof(openusd_string_list_view)),
        invalidMaskData.data(),
        invalidMaskData.size(),
        &maskOffset,
        sizeof(maskOffset),
        1};
    stage = nullptr;
    if (openusd_stage_open_masked(
            compositionControlsPath.string().c_str(), &invalidMaskView, &stage, &error) !=
        OPENUSD_STATUS_INVALID_ARGUMENT)
    {
        openusd_stage_release(stage);
        std::cerr << "Invalid population-mask paths were not rejected.\n";
        return 45;
    }
    std::cout << "Composition controls passed.\n";

    const std::filesystem::path multiSourcePath = directory / "native-multi-source.usda";
    {
        std::ofstream stream(multiSourcePath);
        stream <<
            "#usda 1.0\n"
            "def \"World\" {\n"
            "  def Shader \"A\" {\n"
            "    uniform token info:id = \"TestA\"\n"
            "    float outputs:out = 1\n"
            "  }\n"
            "  def Shader \"B\" {\n"
            "    uniform token info:id = \"TestB\"\n"
            "    float outputs:out = 2\n"
            "  }\n"
            "  def Shader \"Dest\" {\n"
            "    uniform token info:id = \"TestDest\"\n"
            "    float inputs:value.connect = [\n"
            "      </World/A.outputs:out>,\n"
            "      </World/B.outputs:out>\n"
            "    ]\n"
            "  }\n"
            "}\n";
    }
    stage = nullptr;
    status = openusd_stage_open(multiSourcePath.string().c_str(), &stage, &error);
    openusd_string_list* sourcesList = nullptr;
    openusd_string_list_view sourcesView{
        static_cast<uint32_t>(sizeof(openusd_string_list_view)),
        nullptr,
        0,
        nullptr,
        0,
        0};
    openusd_shade_attribute_type singleType = OPENUSD_SHADE_ATTRIBUTE_INVALID;
    const openusd_status multiSourceStatus =
        status == OPENUSD_STATUS_OK
        ? openusd_shade_get_connected_sources(
            stage,
            "/World/Dest",
            "value",
            OPENUSD_SHADE_ATTRIBUTE_INPUT,
            &sourcesList,
            &sourcesView,
            &error)
        : status;
    const bool bulkSourcesValid =
        multiSourceStatus == OPENUSD_STATUS_OK &&
        sourcesView.count == 6 &&
        sourcesView.offsets_size == sourcesView.count * sizeof(size_t);
    openusd_string_list_release(sourcesList);
    sourcesList = nullptr;
    sourcesView = {
        static_cast<uint32_t>(sizeof(openusd_string_list_view)),
        nullptr,
        0,
        nullptr,
        0,
        0};
    const openusd_status singleSourceStatus =
        status == OPENUSD_STATUS_OK
        ? openusd_shade_get_connected_source(
            stage,
            "/World/Dest",
            "value",
            OPENUSD_SHADE_ATTRIBUTE_INPUT,
            &sourcesList,
            &sourcesView,
            &singleType,
            &error)
        : status;
    if (!bulkSourcesValid || singleSourceStatus != OPENUSD_STATUS_INVALID_ARGUMENT)
    {
        openusd_string_list_release(sourcesList);
        openusd_stage_release(stage);
        std::cerr << "Multiple shading sources were not reported safely: bulk="
                  << multiSourceStatus << ", count=" << sourcesView.count
                  << ", single=" << singleSourceStatus << ".\n";
        return 107;
    }
    openusd_string_list_release(sourcesList);
    openusd_stage_release(stage);
    std::cout << "ABI v10 hardening passed.\n";
    return 0;
}
