// Copyright (c) marcschier. Licensed under the MIT License.

#pragma once

#include "openusd_dotnet.h"
#include "openusd_renderer_stage_bridge.h"

#include "pxr/base/gf/bbox3d.h"
#include "pxr/base/gf/camera.h"
#include "pxr/base/gf/frustum.h"
#include "pxr/base/gf/matrix4d.h"
#include "pxr/base/gf/quatf.h"
#include "pxr/base/gf/range1d.h"
#include "pxr/base/gf/range2d.h"
#include "pxr/base/gf/range3d.h"
#include "pxr/base/gf/vec2f.h"
#include "pxr/base/gf/vec3d.h"
#include "pxr/base/gf/vec3f.h"
#include "pxr/base/gf/vec3h.h"
#include "pxr/base/plug/registry.h"
#include "pxr/base/tf/diagnostic.h"
#include "pxr/base/tf/errorMark.h"
#include "pxr/base/tf/notice.h"
#include "pxr/base/tf/weakBase.h"
#include "pxr/base/vt/array.h"
#include "pxr/base/vt/dictionary.h"
#include "pxr/base/vt/value.h"
#include "pxr/imaging/hio/image.h"
#include "pxr/pxr.h"
#include "pxr/usd/pcp/composeSite.h"
#include "pxr/usd/pcp/errors.h"
#include "pxr/usd/sdf/layer.h"
#include "pxr/usd/sdf/assetPath.h"
#include "pxr/usd/sdf/path.h"
#include "pxr/usd/sdf/payload.h"
#include "pxr/usd/sdf/reference.h"
#include "pxr/usd/sdf/types.h"
#include "pxr/usd/usd/attribute.h"
#include "pxr/usd/usd/editTarget.h"
#include "pxr/usd/usd/inherits.h"
#include "pxr/usd/usd/notice.h"
#include "pxr/usd/usd/payloads.h"
#include "pxr/usd/usd/prim.h"
#include "pxr/usd/usd/primRange.h"
#include "pxr/usd/usd/references.h"
#include "pxr/usd/usd/relationship.h"
#include "pxr/usd/usd/resolveInfo.h"
#include "pxr/usd/usd/stage.h"
#include "pxr/usd/usd/stagePopulationMask.h"
#include "pxr/usd/usd/specializes.h"
#include "pxr/usd/usd/variantSets.h"
#include "pxr/usd/usdGeom/bboxCache.h"
#include "pxr/usd/usdGeom/camera.h"
#include "pxr/usd/usdGeom/gprim.h"
#include "pxr/usd/usdGeom/imageable.h"
#include "pxr/usd/usdGeom/mesh.h"
#include "pxr/usd/usdGeom/primvar.h"
#include "pxr/usd/usdGeom/tokens.h"
#include "pxr/usd/usdGeom/xform.h"
#include "pxr/usd/usdGeom/xformCache.h"
#include "pxr/usd/usdGeom/xformable.h"
#include "pxr/usd/usdLux/cylinderLight.h"
#include "pxr/usd/usdLux/diskLight.h"
#include "pxr/usd/usdLux/distantLight.h"
#include "pxr/usd/usdLux/domeLight.h"
#include "pxr/usd/usdLux/lightAPI.h"
#include "pxr/usd/usdLux/rectLight.h"
#include "pxr/usd/usdLux/shapingAPI.h"
#include "pxr/usd/usdLux/sphereLight.h"
#include "pxr/usd/usdShade/connectableAPI.h"
#include "pxr/usd/usdShade/input.h"
#include "pxr/usd/usdShade/material.h"
#include "pxr/usd/usdShade/materialBindingAPI.h"
#include "pxr/usd/usdShade/output.h"
#include "pxr/usd/usdShade/shader.h"
#include "pxr/usd/usdSkel/animation.h"
#include "pxr/usd/usdSkel/bindingAPI.h"
#include "pxr/usd/usdSkel/root.h"
#include "pxr/usd/usdSkel/skeleton.h"
#include "pxr/usd/usdSkel/topology.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <exception>
#include <limits>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <string_view>
#include <thread>
#include <unordered_set>
#include <utility>
#include <vector>

PXR_NAMESPACE_USING_DIRECTIVE

extern std::atomic<size_t> DiagnosticLiveStageCoreCount;
extern std::atomic<size_t> DiagnosticPeakStageCoreCount;
#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
extern std::atomic<size_t> TestDestroyedStageCoreCount;
#endif

inline void UpdatePeak(std::atomic<size_t>& peak, size_t value) noexcept
{
    size_t current = peak.load(std::memory_order_relaxed);
    while (current < value &&
           !peak.compare_exchange_weak(
               current,
               value,
               std::memory_order_relaxed,
               std::memory_order_relaxed))
    {
    }
}

class StageCoreDiagnosticLifetime final
{
public:
    StageCoreDiagnosticLifetime()
    {
        const size_t live =
            DiagnosticLiveStageCoreCount.fetch_add(1, std::memory_order_relaxed) + 1;
        UpdatePeak(DiagnosticPeakStageCoreCount, live);
    }

    ~StageCoreDiagnosticLifetime()
    {
#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
        TestDestroyedStageCoreCount.fetch_add(1, std::memory_order_relaxed);
#endif
        DiagnosticLiveStageCoreCount.fetch_sub(1, std::memory_order_relaxed);
    }
};

class StageNoticeListener final : public TfWeakBase
{
public:
    StageNoticeListener(
        const UsdStageRefPtr& stage,
        std::atomic<uint64_t>* serial)
        : _serial(serial)
        , _key(TfNotice::Register(
            TfCreateWeakPtr(this),
            &StageNoticeListener::_OnObjectsChanged,
            UsdStageConstPtr(stage)))
    {
    }

    ~StageNoticeListener()
    {
        TfNotice::RevokeAndWait(_key);
    }

private:
    void _OnObjectsChanged(const UsdNotice::ObjectsChanged&)
    {
        _serial->fetch_add(1, std::memory_order_relaxed);
    }

    std::atomic<uint64_t>* _serial;
    TfNotice::Key _key;
};

struct openusd_stage
{
    explicit openusd_stage(UsdStageRefPtr stage)
        : value(std::move(stage))
        , listener(std::make_unique<StageNoticeListener>(value, &change_serial))
    {
    }

    StageCoreDiagnosticLifetime diagnostic_lifetime;
    std::atomic<size_t> reference_count{1};
    mutable std::recursive_mutex mutex;
    UsdStageRefPtr value;
    std::atomic<uint64_t> change_serial{0};
    std::unique_ptr<StageNoticeListener> listener;
};

struct openusd_stage_access
{
    explicit openusd_stage_access(openusd_stage* retained_stage)
        : stage(retained_stage)
        , lock(stage->mutex)
        , owner(std::this_thread::get_id())
    {
    }

    openusd_stage* stage;
    std::unique_lock<std::recursive_mutex> lock;
    std::thread::id owner;
};

struct openusd_layer
{
    SdfLayerHandle value;
    openusd_stage* stage = nullptr;
};

struct openusd_string_list
{
    std::vector<char> data;
    std::vector<size_t> offsets;
};

struct openusd_payload_arc_list
{
    std::vector<char> data;
    std::vector<size_t> offsets;
};

namespace
{
constexpr uint32_t DataAbiVersion = 11;
constexpr uint64_t DataCapabilities =
    OPENUSD_CAPABILITY_STRING_LIST_V2 |
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
    OPENUSD_CAPABILITY_USD_PHYSICS_SCHEMA;
static_assert(sizeof(openusd_error_buffer) == sizeof(void*) * 3);
static_assert(offsetof(openusd_error_buffer, data) == 0);
static_assert(offsetof(openusd_error_buffer, capacity) == sizeof(void*));
static_assert(offsetof(openusd_error_buffer, required) == sizeof(void*) * 2);
static_assert(sizeof(openusd_string_list_view) == sizeof(void*) * 6);
static_assert(offsetof(openusd_string_list_view, struct_size) == 0);
static_assert(offsetof(openusd_string_list_view, data) == sizeof(void*));
static_assert(offsetof(openusd_string_list_view, data_size) == sizeof(void*) * 2);
static_assert(offsetof(openusd_string_list_view, offsets) == sizeof(void*) * 3);
static_assert(offsetof(openusd_string_list_view, offsets_size) == sizeof(void*) * 4);
static_assert(offsetof(openusd_string_list_view, count) == sizeof(void*) * 5);
static_assert(sizeof(openusd_payload_arc_list_view) == sizeof(uint32_t) * 2 + sizeof(void*) * 5);
static_assert(offsetof(openusd_payload_arc_list_view, struct_size) == 0);
static_assert(offsetof(openusd_payload_arc_list_view, version) == sizeof(uint32_t));
static_assert(offsetof(openusd_payload_arc_list_view, data) == sizeof(uint32_t) * 2);
static_assert(
    offsetof(openusd_payload_arc_list_view, data_size) ==
    sizeof(uint32_t) * 2 + sizeof(void*));
static_assert(
    offsetof(openusd_payload_arc_list_view, offsets) ==
    sizeof(uint32_t) * 2 + sizeof(void*) * 2);
static_assert(
    offsetof(openusd_payload_arc_list_view, offsets_size) ==
    sizeof(uint32_t) * 2 + sizeof(void*) * 3);
static_assert(
    offsetof(openusd_payload_arc_list_view, count) ==
    sizeof(uint32_t) * 2 + sizeof(void*) * 4);
static_assert(sizeof(openusd_image_info) == sizeof(uint32_t) * 4);
static_assert(offsetof(openusd_image_info, struct_size) == 0);
static_assert(offsetof(openusd_image_info, version) == sizeof(uint32_t));
static_assert(offsetof(openusd_image_info, width) == sizeof(uint32_t) * 2);
static_assert(offsetof(openusd_image_info, height) == sizeof(uint32_t) * 3);
static_assert(sizeof(openusd_vec2f) == sizeof(float) * 2);
static_assert(offsetof(openusd_vec2f, x) == 0);
static_assert(offsetof(openusd_vec2f, y) == sizeof(float));
static_assert(sizeof(openusd_vec3f) == sizeof(float) * 3);
static_assert(offsetof(openusd_vec3f, z) == sizeof(float) * 2);
static_assert(sizeof(openusd_quatf) == sizeof(float) * 4);
static_assert(offsetof(openusd_quatf, real) == 0);
static_assert(offsetof(openusd_quatf, z) == sizeof(float) * 3);
static_assert(sizeof(openusd_matrix4d) == sizeof(double) * 16);
static_assert(offsetof(openusd_matrix4d, values) == 0);
static_assert(sizeof(openusd_extent3f) == sizeof(float) * 6);
static_assert(offsetof(openusd_extent3f, minimum) == 0);
static_assert(offsetof(openusd_extent3f, maximum) == sizeof(openusd_vec3f));
static_assert(sizeof(openusd_bounds3d) == 64);
static_assert(alignof(openusd_bounds3d) == alignof(double));
static_assert(offsetof(openusd_bounds3d, struct_size) == 0);
static_assert(offsetof(openusd_bounds3d, version) == sizeof(uint32_t));
static_assert(offsetof(openusd_bounds3d, is_valid) == sizeof(uint32_t) * 2);
static_assert(offsetof(openusd_bounds3d, is_empty) == sizeof(uint32_t) * 3);
static_assert(offsetof(openusd_bounds3d, minimum) == 16);
static_assert(offsetof(openusd_bounds3d, maximum) == 40);
static_assert(sizeof(openusd_geom_camera_state) == 120);
static_assert(alignof(openusd_geom_camera_state) == alignof(double));
static_assert(offsetof(openusd_geom_camera_state, struct_size) == 0);
static_assert(offsetof(openusd_geom_camera_state, version) == 4);
static_assert(offsetof(openusd_geom_camera_state, is_valid) == 8);
static_assert(offsetof(openusd_geom_camera_state, projection) == 12);
static_assert(offsetof(openusd_geom_camera_state, window_left) == 16);
static_assert(offsetof(openusd_geom_camera_state, clipping_near) == 48);
static_assert(offsetof(openusd_geom_camera_state, focal_length) == 64);
static_assert(offsetof(openusd_geom_camera_state, horizontal_aperture_offset) == 88);
static_assert(offsetof(openusd_geom_camera_state, focus_distance) == 104);
static_assert(offsetof(openusd_geom_camera_state, f_stop) == 112);
static_assert(sizeof(openusd_metadata_value) == 32);
static_assert(offsetof(openusd_metadata_value, int64_value) == 16);
static_assert(offsetof(openusd_metadata_value, double_value) == 24);
static_assert(sizeof(openusd_scalar_value) == 176);
static_assert(offsetof(openusd_scalar_value, int64_value) == 16);
static_assert(offsetof(openusd_scalar_value, vec3f_value) == 32);
static_assert(offsetof(openusd_scalar_value, matrix4d_value) == 48);

openusd_status CopyString(
    const std::string& value,
    char* buffer,
    size_t capacity,
    size_t* required)
{
    if (required == nullptr)
    {
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    *required = value.size() + 1;
    if (buffer == nullptr || capacity < *required)
    {
        return OPENUSD_STATUS_BUFFER_TOO_SMALL;
    }

    std::memcpy(buffer, value.c_str(), *required);
    return OPENUSD_STATUS_OK;
}

void WriteError(openusd_error_buffer* error, std::string_view message) noexcept
{
    if (error == nullptr)
    {
        return;
    }

    error->required = message.size() + 1;
    if (error->data == nullptr || error->capacity == 0)
    {
        return;
    }

    const size_t count = std::min(message.size(), error->capacity - 1);
    std::memcpy(error->data, message.data(), count);
    error->data[count] = '\0';
}

template <typename TValue>
void ResetAbiOutput(TValue* output) noexcept
{
    if (output != nullptr)
    {
        const TValue value{};
        std::memcpy(output, &value, sizeof(value));
    }
}

template <typename TValue>
void ResetVersionedAbiOutput(TValue* output) noexcept
{
    if (output == nullptr)
    {
        return;
    }

    uint32_t struct_size = 0;
    std::memcpy(&struct_size, output, sizeof(struct_size));
    const size_t writable_size = std::min<size_t>(struct_size, sizeof(TValue));
    if (writable_size == 0)
    {
        return;
    }

    std::memset(output, 0, writable_size);
    if (writable_size >= sizeof(uint32_t))
    {
        std::memcpy(output, &struct_size, sizeof(struct_size));
    }
}

void ResetBounds3dOutput(openusd_bounds3d* output) noexcept
{
    if (output == nullptr)
    {
        return;
    }

    uint32_t struct_size = 0;
    std::memcpy(&struct_size, output, sizeof(struct_size));
    const size_t writable_size = std::min<size_t>(struct_size, sizeof(openusd_bounds3d));
    if (writable_size == 0)
    {
        return;
    }

    std::memset(output, 0, writable_size);
    if (writable_size >= sizeof(uint32_t))
    {
        std::memcpy(output, &struct_size, sizeof(struct_size));
    }
    if (writable_size >= offsetof(openusd_bounds3d, version) + sizeof(uint32_t))
    {
        const uint32_t version = OPENUSD_BOUNDS3D_VERSION;
        std::memcpy(
            reinterpret_cast<unsigned char*>(output) +
                offsetof(openusd_bounds3d, version),
            &version,
            sizeof(version));
    }
    if (writable_size >= offsetof(openusd_bounds3d, is_empty) + sizeof(int32_t))
    {
        const int32_t is_empty = 1;
        std::memcpy(
            reinterpret_cast<unsigned char*>(output) +
                offsetof(openusd_bounds3d, is_empty),
            &is_empty,
            sizeof(is_empty));
    }
}

class Bounds3dFailureReset final
{
public:
    explicit Bounds3dFailureReset(openusd_bounds3d* output) noexcept
        : _output(output)
    {
    }

    ~Bounds3dFailureReset()
    {
        if (!_committed)
        {
            ResetBounds3dOutput(_output);
        }
    }

    void Commit() noexcept
    {
        _committed = true;
    }

private:
    openusd_bounds3d* _output;
    bool _committed = false;
};

void ResetCameraStateOutput(openusd_geom_camera_state* output) noexcept
{
    if (output == nullptr)
    {
        return;
    }

    uint32_t struct_size = 0;
    std::memcpy(&struct_size, output, sizeof(struct_size));
    const size_t writable_size =
        std::min<size_t>(struct_size, sizeof(openusd_geom_camera_state));
    if (writable_size == 0)
    {
        return;
    }

    std::memset(output, 0, writable_size);
    if (writable_size >= sizeof(uint32_t))
    {
        std::memcpy(output, &struct_size, sizeof(struct_size));
    }
    if (writable_size >=
        offsetof(openusd_geom_camera_state, version) + sizeof(uint32_t))
    {
        const uint32_t version = OPENUSD_GEOM_CAMERA_STATE_VERSION;
        std::memcpy(
            reinterpret_cast<unsigned char*>(output) +
                offsetof(openusd_geom_camera_state, version),
            &version,
            sizeof(version));
    }
}

class CameraStateFailureReset final
{
public:
    explicit CameraStateFailureReset(openusd_geom_camera_state* output) noexcept
        : _output(output)
    {
    }

    ~CameraStateFailureReset()
    {
        if (!_committed)
        {
            ResetCameraStateOutput(_output);
        }
    }

    void Commit() noexcept
    {
        _committed = true;
    }

private:
    openusd_geom_camera_state* _output;
    bool _committed = false;
};

void ResetAbiStringOutput(char* buffer, size_t capacity) noexcept
{
    if (buffer != nullptr && capacity != 0)
    {
        buffer[0] = '\0';
    }
}

template <typename TValue>
void ResetAbiWritableBuffer(TValue* buffer, size_t capacity) noexcept
{
    if (buffer == nullptr || capacity == 0 ||
        capacity > std::numeric_limits<size_t>::max() / sizeof(TValue))
    {
        return;
    }
    std::memset(buffer, 0, capacity * sizeof(TValue));
}

template <typename TValue>
class AbiWritableBufferFailureReset final
{
public:
    AbiWritableBufferFailureReset(TValue* buffer, size_t capacity) noexcept
        : _buffer(buffer), _capacity(capacity)
    {
    }

    ~AbiWritableBufferFailureReset()
    {
        if (!_committed)
        {
            ResetAbiWritableBuffer(_buffer, _capacity);
        }
    }

    void Commit() noexcept
    {
        _committed = true;
    }

private:
    TValue* _buffer;
    size_t _capacity;
    bool _committed = false;
};

template <typename TValue, typename TAction>
openusd_status WithAbiWritableBuffer(
    TValue* buffer,
    size_t capacity,
    TAction&& action)
{
    AbiWritableBufferFailureReset<TValue> reset(buffer, capacity);
    const openusd_status status = action();
    if (status == OPENUSD_STATUS_OK)
    {
        reset.Commit();
    }
    return status;
}

template <typename TFirst, typename TSecond, typename TAction>
openusd_status WithAbiWritableBuffers(
    TFirst* first_buffer,
    size_t first_capacity,
    TSecond* second_buffer,
    size_t second_capacity,
    TAction&& action)
{
    AbiWritableBufferFailureReset<TFirst> first_reset(first_buffer, first_capacity);
    AbiWritableBufferFailureReset<TSecond> second_reset(second_buffer, second_capacity);
    const openusd_status status = action();
    if (status == OPENUSD_STATUS_OK)
    {
        first_reset.Commit();
        second_reset.Commit();
    }
    return status;
}

std::string ConsumeErrors(TfErrorMark& mark);

template <typename TAction>
openusd_status Guard(openusd_error_buffer* error, TAction&& action)
{
    try
    {
        if (error != nullptr)
        {
            error->required = 0;
            if (error->data != nullptr && error->capacity != 0)
            {
                error->data[0] = '\0';
            }
        }

        TfErrorMark mark;
        const openusd_status status = action();
        if (!mark.IsClean())
        {
            const std::string message = ConsumeErrors(mark);
            WriteError(
                error,
                message.empty() ? std::string_view("OpenUSD reported a native error.") : message);
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        return status;
    }
    catch (const std::exception& exception)
    {
        WriteError(error, exception.what());
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    catch (...)
    {
        WriteError(error, "Unknown native exception.");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
}

bool RetainStageReference(openusd_stage* stage) noexcept
{
    if (stage == nullptr)
    {
        return false;
    }

    size_t count = stage->reference_count.load(std::memory_order_relaxed);
    while (count != 0 && count != std::numeric_limits<size_t>::max())
    {
        if (stage->reference_count.compare_exchange_weak(
                count,
                count + 1,
                std::memory_order_acquire,
                std::memory_order_relaxed))
        {
            return true;
        }
    }
    return false;
}

void ReleaseStageReference(openusd_stage* stage) noexcept
{
    if (stage == nullptr)
    {
        return;
    }

    size_t count = stage->reference_count.load(std::memory_order_relaxed);
    while (count != 0)
    {
        if (stage->reference_count.compare_exchange_weak(
                count,
                count - 1,
                std::memory_order_release,
                std::memory_order_relaxed))
        {
            if (count == 1)
            {
                std::atomic_thread_fence(std::memory_order_acquire);
                try
                {
                    delete stage;
                }
                catch (...)
                {
                }
            }
            return;
        }
    }
}

bool IsStageAccessBeginFailpoint(const char* name) noexcept
{
#if !defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
    static_cast<void>(name);
    return false;
#elif defined(_WIN32)
    char* value = nullptr;
    size_t value_size = 0;
    if (_dupenv_s(
            &value,
            &value_size,
            "OPENUSD_DOTNET_STAGE_ACCESS_BEGIN_FAILPOINT") != 0)
    {
        return false;
    }
    const bool matches = value != nullptr && std::strcmp(value, name) == 0;
    std::free(value);
    return matches;
#else
    const char* value = std::getenv("OPENUSD_DOTNET_STAGE_ACCESS_BEGIN_FAILPOINT");
    return value != nullptr && std::strcmp(value, name) == 0;
#endif
}

bool IsCompositionEnumerationFailpoint(const char* name) noexcept
{
#if !defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
    static_cast<void>(name);
    return false;
#elif defined(_WIN32)
    char* value = nullptr;
    size_t value_size = 0;
    if (_dupenv_s(
            &value,
            &value_size,
            "OPENUSD_DOTNET_COMPOSITION_ENUMERATION_FAILPOINT") != 0)
    {
        return false;
    }
    const bool matches = value != nullptr && std::strcmp(value, name) == 0;
    std::free(value);
    return matches;
#else
    const char* value = std::getenv("OPENUSD_DOTNET_COMPOSITION_ENUMERATION_FAILPOINT");
    return value != nullptr && std::strcmp(value, name) == 0;
#endif
}

bool IsWorldTransformFailpoint(const char* name) noexcept
{
#if !defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
    static_cast<void>(name);
    return false;
#elif defined(_WIN32)
    char* value = nullptr;
    size_t value_size = 0;
    if (_dupenv_s(
            &value,
            &value_size,
            "OPENUSD_DOTNET_WORLD_TRANSFORM_FAILPOINT") != 0)
    {
        return false;
    }
    const bool matches = value != nullptr && std::strcmp(value, name) == 0;
    std::free(value);
    return matches;
#else
    const char* value = std::getenv("OPENUSD_DOTNET_WORLD_TRANSFORM_FAILPOINT");
    return value != nullptr && std::strcmp(value, name) == 0;
#endif
}

bool IsCameraStateFailpoint(const char* name) noexcept
{
#if !defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
    static_cast<void>(name);
    return false;
#elif defined(_WIN32)
    char* value = nullptr;
    size_t value_size = 0;
    if (_dupenv_s(
            &value,
            &value_size,
            "OPENUSD_DOTNET_CAMERA_STATE_FAILPOINT") != 0)
    {
        return false;
    }
    const bool matches = value != nullptr && std::strcmp(value, name) == 0;
    std::free(value);
    return matches;
#else
    const char* value = std::getenv("OPENUSD_DOTNET_CAMERA_STATE_FAILPOINT");
    return value != nullptr && std::strcmp(value, name) == 0;
#endif
}

template <typename TAction>
openusd_status GuardStage(
    const openusd_stage* stage,
    openusd_error_buffer* error,
    TAction&& action)
{
    return Guard(error, [&]()
    {
        if (stage == nullptr)
        {
            return action();
        }
        std::lock_guard<std::recursive_mutex> lock(stage->mutex);
        return action();
    });
}

template <typename TAction>
openusd_status GuardLayer(
    const openusd_layer* layer,
    openusd_error_buffer* error,
    TAction&& action)
{
    return Guard(error, [&]()
    {
        if (layer == nullptr || layer->stage == nullptr)
        {
            return action();
        }
        std::lock_guard<std::recursive_mutex> lock(layer->stage->mutex);
        return action();
    });
}

void ReleaseLayer(openusd_layer* layer) noexcept
{
    if (layer == nullptr)
    {
        return;
    }

    openusd_stage* stage = layer->stage;
    if (stage != nullptr)
    {
        try
        {
            std::lock_guard<std::recursive_mutex> lock(stage->mutex);
            layer->value = SdfLayerHandle();
        }
        catch (...)
        {
            layer->value = SdfLayerHandle();
        }
    }
    delete layer;
    ReleaseStageReference(stage);
}

void FinalizeStageAccess(openusd_stage_access* access) noexcept
{
    openusd_stage* stage = access->stage;
    try
    {
        access->lock.unlock();
    }
    catch (...)
    {
        std::terminate();
    }
    delete access;
    ReleaseStageReference(stage);
}

std::string ConsumeErrors(TfErrorMark& mark)
{
    std::string message;
    for (const TfError& error : mark)
    {
        if (!message.empty())
        {
            message += '\n';
        }
        message += error.GetCommentary();
    }
    mark.Clear();
    return message;
}

UsdTimeCode GetTimeCode(int32_t time_sampled, double time_code)
{
    return time_sampled != 0 ? UsdTimeCode(time_code) : UsdTimeCode::Default();
}

bool IsValidPrimPath(const char* value);

template <typename TValue>
bool IsAligned(const TValue* values)
{
    return values == nullptr ||
        reinterpret_cast<uintptr_t>(values) % alignof(TValue) == 0;
}

template <typename TValue>
bool IsValidArrayBuffer(const TValue* values, size_t count)
{
    return (values != nullptr || count == 0) &&
        IsAligned(values) &&
        count <= std::numeric_limits<size_t>::max() / sizeof(TValue);
}

openusd_status ValidateAttributeType(
    const UsdAttribute& attribute,
    const SdfValueTypeName& expected,
    const char* label,
    openusd_error_buffer* error)
{
    if (!attribute || attribute.GetTypeName() != expected)
    {
        WriteError(error, std::string("The attribute is not an exact ") + label + " value.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    return OPENUSD_STATUS_OK;
}

template <typename TNative, typename TUsd, typename TCompatible, typename TConvert>
openusd_status SetArrayAttribute(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const TNative* values,
    size_t count,
    int32_t time_sampled,
    double time_code,
    const SdfValueTypeName& type_name,
    const char* type_label,
    TCompatible&& compatible,
    TConvert&& convert,
    openusd_error_buffer* error)
{
    if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
        attribute_name == nullptr || attribute_name[0] == '\0' ||
        !IsValidArrayBuffer(values, count))
    {
        WriteError(error, "A valid stage, prim path, attribute name, aligned buffer, and count are required.");
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

        TfErrorMark mark;
        const TfToken name(attribute_name);
        UsdAttribute attribute = prim.GetAttribute(name);
        if (attribute && !compatible(attribute.GetTypeName()))
        {
            WriteError(
                error,
                std::string("The attribute is not a ") + type_label + " array.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (!attribute)
        {
            attribute = prim.CreateAttribute(name, type_name, true);
        }

        VtArray<TUsd> array(count);
        for (size_t index = 0; index < count; ++index)
        {
            array[index] = convert(values[index]);
        }
        const bool set = attribute && attribute.Set(array, GetTimeCode(time_sampled, time_code));
        if (!set || !mark.IsClean())
        {
            std::string message = ConsumeErrors(mark);
            WriteError(
                error,
                message.empty()
                    ? std::string("Could not set the ") + type_label + " array attribute."
                    : message);
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        return OPENUSD_STATUS_OK;
    });
}

template <typename TNative, typename TUsd, typename TCompatible, typename TConvert>
openusd_status GetArrayAttribute(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    TNative* values,
    size_t capacity,
    size_t* required,
    const char* type_label,
    TCompatible&& compatible,
    TConvert&& convert,
    openusd_error_buffer* error)
{
    if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
        attribute_name == nullptr || attribute_name[0] == '\0' ||
        required == nullptr || !IsValidArrayBuffer(values, capacity))
    {
        WriteError(error, "A valid stage, prim path, attribute name, aligned buffer, and size output are required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    return Guard(error, [&]()
    {
        const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
        const UsdAttribute attribute =
            prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
        if (!attribute)
        {
            WriteError(error, std::string("The requested ") + type_label + " array was not found.");
            return OPENUSD_STATUS_NOT_FOUND;
        }
        if (!compatible(attribute.GetTypeName()))
        {
            WriteError(error, std::string("The attribute is not a ") + type_label + " array.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        TfErrorMark mark;
        VtArray<TUsd> array;
        const UsdTimeCode time = GetTimeCode(time_sampled, time_code);
        const bool read = attribute.Get(&array, time);
        if (!read || !mark.IsClean())
        {
            const bool had_errors = !mark.IsClean();
            std::string message = ConsumeErrors(mark);
            if (message.empty())
            {
                message = attribute.GetResolveInfo(time).ValueIsBlocked()
                    ? "The attribute value is blocked."
                    : "The attribute has no readable array value.";
            }
            WriteError(error, message);
            return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
        }

        const size_t count = array.size();
        if (values == nullptr && capacity == 0)
        {
            *required = count;
            return OPENUSD_STATUS_OK;
        }
        if (count == 0)
        {
            *required = 0;
            return OPENUSD_STATUS_OK;
        }
        if (values == nullptr || capacity < count)
        {
            return OPENUSD_STATUS_BUFFER_TOO_SMALL;
        }
        for (size_t index = 0; index < count; ++index)
        {
            values[index] = convert(array[index]);
        }
        *required = count;
        return OPENUSD_STATUS_OK;
    });
}

template <typename TSchema>
openusd_status GetGeomSchema(
    const openusd_stage* stage,
    const char* prim_path,
    const char* schema_name,
    TSchema* schema,
    openusd_error_buffer* error)
{
    if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) || schema == nullptr)
    {
        WriteError(error, "A valid stage, absolute prim path, and schema output are required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
    if (!prim)
    {
        WriteError(error, std::string("Prim was not found: ") + prim_path);
        return OPENUSD_STATUS_NOT_FOUND;
    }

    *schema = TSchema(prim);
    if (!*schema)
    {
        WriteError(
            error,
            std::string("The prim is not compatible with ") + schema_name + ": " + prim_path);
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    return OPENUSD_STATUS_OK;
}

bool GetVisibilityToken(int32_t value, TfToken* token)
{
    if (value == OPENUSD_GEOM_VISIBILITY_INHERITED)
    {
        *token = UsdGeomTokens->inherited;
        return true;
    }
    if (value == OPENUSD_GEOM_VISIBILITY_INVISIBLE)
    {
        *token = UsdGeomTokens->invisible;
        return true;
    }
    return false;
}

bool GetVisibilityValue(const TfToken& token, int32_t* value)
{
    if (token == UsdGeomTokens->inherited)
    {
        *value = OPENUSD_GEOM_VISIBILITY_INHERITED;
        return true;
    }
    if (token == UsdGeomTokens->invisible)
    {
        *value = OPENUSD_GEOM_VISIBILITY_INVISIBLE;
        return true;
    }
    return false;
}

bool GetPurposeToken(int32_t value, TfToken* token)
{
    switch (value)
    {
        case OPENUSD_GEOM_PURPOSE_DEFAULT:
            *token = UsdGeomTokens->default_;
            return true;
        case OPENUSD_GEOM_PURPOSE_RENDER:
            *token = UsdGeomTokens->render;
            return true;
        case OPENUSD_GEOM_PURPOSE_PROXY:
            *token = UsdGeomTokens->proxy;
            return true;
        case OPENUSD_GEOM_PURPOSE_GUIDE:
            *token = UsdGeomTokens->guide;
            return true;
        default:
            return false;
    }
}

bool GetPurposeValue(const TfToken& token, int32_t* value)
{
    if (token == UsdGeomTokens->default_)
    {
        *value = OPENUSD_GEOM_PURPOSE_DEFAULT;
        return true;
    }
    if (token == UsdGeomTokens->render)
    {
        *value = OPENUSD_GEOM_PURPOSE_RENDER;
        return true;
    }
    if (token == UsdGeomTokens->proxy)
    {
        *value = OPENUSD_GEOM_PURPOSE_PROXY;
        return true;
    }
    if (token == UsdGeomTokens->guide)
    {
        *value = OPENUSD_GEOM_PURPOSE_GUIDE;
        return true;
    }
    return false;
}

bool GetInterpolationToken(int32_t value, TfToken* token)
{
    switch (value)
    {
        case OPENUSD_GEOM_INTERPOLATION_CONSTANT:
            *token = UsdGeomTokens->constant;
            return true;
        case OPENUSD_GEOM_INTERPOLATION_UNIFORM:
            *token = UsdGeomTokens->uniform;
            return true;
        case OPENUSD_GEOM_INTERPOLATION_VARYING:
            *token = UsdGeomTokens->varying;
            return true;
        case OPENUSD_GEOM_INTERPOLATION_VERTEX:
            *token = UsdGeomTokens->vertex;
            return true;
        case OPENUSD_GEOM_INTERPOLATION_FACE_VARYING:
            *token = UsdGeomTokens->faceVarying;
            return true;
        default:
            return false;
    }
}

bool GetInterpolationValue(const TfToken& token, int32_t* value)
{
    const TfToken tokens[] = {
        UsdGeomTokens->constant,
        UsdGeomTokens->uniform,
        UsdGeomTokens->varying,
        UsdGeomTokens->vertex,
        UsdGeomTokens->faceVarying};
    for (int32_t index = 0; index < 5; ++index)
    {
        if (token == tokens[index])
        {
            *value = index;
            return true;
        }
    }
    return false;
}

openusd_status ValidateMeshNormalsCardinality(
    const UsdGeomMesh& mesh,
    const TfToken& interpolation,
    size_t normal_count,
    const UsdTimeCode& time,
    openusd_error_buffer* error)
{
    size_t expected = 0;
    TfErrorMark mark;
    if (interpolation == UsdGeomTokens->constant)
    {
        expected = 1;
    }
    else if (interpolation == UsdGeomTokens->uniform)
    {
        VtIntArray counts;
        if (!mesh.GetFaceVertexCountsAttr().Get(&counts, time))
        {
            const bool had_errors = !mark.IsClean();
            std::string message = ConsumeErrors(mark);
            WriteError(
                error,
                message.empty() ? "Mesh face counts are required to validate uniform normals."
                                : message);
            return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        expected = counts.size();
    }
    else if (
        interpolation == UsdGeomTokens->vertex ||
        interpolation == UsdGeomTokens->varying)
    {
        VtVec3fArray points;
        if (!mesh.GetPointsAttr().Get(&points, time))
        {
            const bool had_errors = !mark.IsClean();
            std::string message = ConsumeErrors(mark);
            WriteError(
                error,
                message.empty() ? "Mesh points are required to validate vertex or varying normals."
                                : message);
            return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        expected = points.size();
    }
    else
    {
        VtIntArray counts;
        VtIntArray indices;
        if (!mesh.GetFaceVertexCountsAttr().Get(&counts, time) ||
            !mesh.GetFaceVertexIndicesAttr().Get(&indices, time))
        {
            const bool had_errors = !mark.IsClean();
            std::string message = ConsumeErrors(mark);
            WriteError(
                error,
                message.empty()
                    ? "Mesh topology is required to validate face-varying normals."
                    : message);
            return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        for (int count : counts)
        {
            if (count < 0 ||
                expected > std::numeric_limits<size_t>::max() -
                    static_cast<size_t>(count))
            {
                WriteError(error, "Mesh face counts are invalid.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            expected += static_cast<size_t>(count);
        }
        if (expected != indices.size())
        {
            WriteError(error, "Mesh topology has inconsistent face counts and indices.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
    }

    if (!mark.IsClean())
    {
        std::string message = ConsumeErrors(mark);
        WriteError(error, message.empty() ? "Could not validate mesh normals." : message);
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    if (normal_count != expected)
    {
        WriteError(
            error,
            "The normal count does not match the requested interpolation and sampled mesh data.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    return OPENUSD_STATUS_OK;
}

bool GetSubdivisionToken(int32_t value, TfToken* token)
{
    switch (value)
    {
        case OPENUSD_GEOM_SUBDIVISION_NONE:
            *token = UsdGeomTokens->none;
            return true;
        case OPENUSD_GEOM_SUBDIVISION_CATMULL_CLARK:
            *token = UsdGeomTokens->catmullClark;
            return true;
        case OPENUSD_GEOM_SUBDIVISION_LOOP:
            *token = UsdGeomTokens->loop;
            return true;
        case OPENUSD_GEOM_SUBDIVISION_BILINEAR:
            *token = UsdGeomTokens->bilinear;
            return true;
        default:
            return false;
    }
}

bool GetSubdivisionValue(const TfToken& token, int32_t* value)
{
    if (token == UsdGeomTokens->none)
    {
        *value = OPENUSD_GEOM_SUBDIVISION_NONE;
        return true;
    }
    if (token == UsdGeomTokens->catmullClark)
    {
        *value = OPENUSD_GEOM_SUBDIVISION_CATMULL_CLARK;
        return true;
    }
    if (token == UsdGeomTokens->loop)
    {
        *value = OPENUSD_GEOM_SUBDIVISION_LOOP;
        return true;
    }
    if (token == UsdGeomTokens->bilinear)
    {
        *value = OPENUSD_GEOM_SUBDIVISION_BILINEAR;
        return true;
    }
    return false;
}

bool GetOrientationToken(int32_t value, TfToken* token)
{
    if (value == OPENUSD_GEOM_ORIENTATION_RIGHT_HANDED)
    {
        *token = UsdGeomTokens->rightHanded;
        return true;
    }
    if (value == OPENUSD_GEOM_ORIENTATION_LEFT_HANDED)
    {
        *token = UsdGeomTokens->leftHanded;
        return true;
    }
    return false;
}

bool GetOrientationValue(const TfToken& token, int32_t* value)
{
    if (token == UsdGeomTokens->rightHanded)
    {
        *value = OPENUSD_GEOM_ORIENTATION_RIGHT_HANDED;
        return true;
    }
    if (token == UsdGeomTokens->leftHanded)
    {
        *value = OPENUSD_GEOM_ORIENTATION_LEFT_HANDED;
        return true;
    }
    return false;
}

bool GetProjectionToken(int32_t value, TfToken* token)
{
    if (value == OPENUSD_GEOM_CAMERA_PERSPECTIVE)
    {
        *token = UsdGeomTokens->perspective;
        return true;
    }
    if (value == OPENUSD_GEOM_CAMERA_ORTHOGRAPHIC)
    {
        *token = UsdGeomTokens->orthographic;
        return true;
    }
    return false;
}

bool GetProjectionValue(const TfToken& token, int32_t* value)
{
    if (token == UsdGeomTokens->perspective)
    {
        *value = OPENUSD_GEOM_CAMERA_PERSPECTIVE;
        return true;
    }
    if (token == UsdGeomTokens->orthographic)
    {
        *value = OPENUSD_GEOM_CAMERA_ORTHOGRAPHIC;
        return true;
    }
    return false;
}

template <typename TNative, typename TUsd, typename TConvert>
openusd_status SetSchemaArray(
    const UsdAttribute& attribute,
    const TNative* values,
    size_t count,
    const UsdTimeCode& time,
    const SdfValueTypeName& expected_type,
    const char* label,
    TConvert&& convert,
    openusd_error_buffer* error)
{
    const openusd_status type_status =
        ValidateAttributeType(attribute, expected_type, label, error);
    if (type_status != OPENUSD_STATUS_OK)
    {
        return type_status;
    }
    if (!IsValidArrayBuffer(values, count))
    {
        WriteError(error, "An aligned value buffer and non-overflowing count are required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    VtArray<TUsd> array(count);
    for (size_t index = 0; index < count; ++index)
    {
        array[index] = convert(values[index]);
    }
    TfErrorMark mark;
    const bool set = attribute && attribute.Set(array, time);
    if (!set || !mark.IsClean())
    {
        std::string message = ConsumeErrors(mark);
        WriteError(error, message.empty() ? std::string("Could not set ") + label + "." : message);
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    return OPENUSD_STATUS_OK;
}

template <typename TNative, typename TUsd, typename TConvert>
openusd_status GetSchemaArray(
    const UsdAttribute& attribute,
    const UsdTimeCode& time,
    TNative* values,
    size_t capacity,
    size_t* required,
    const SdfValueTypeName& expected_type,
    const char* label,
    TConvert&& convert,
    openusd_error_buffer* error)
{
    const openusd_status type_status =
        ValidateAttributeType(attribute, expected_type, label, error);
    if (type_status != OPENUSD_STATUS_OK)
    {
        return type_status;
    }
    if (required == nullptr || !IsValidArrayBuffer(values, capacity))
    {
        WriteError(error, "An aligned output buffer and size output are required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    TfErrorMark mark;
    VtArray<TUsd> array;
    const bool read = attribute && attribute.Get(&array, time);
    if (!read || !mark.IsClean())
    {
        const bool had_errors = !mark.IsClean();
        std::string message = ConsumeErrors(mark);
        WriteError(error, message.empty() ? std::string("Could not read ") + label + "." : message);
        return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
    }
    const size_t count = array.size();
    if (values == nullptr && capacity == 0)
    {
        *required = count;
        return OPENUSD_STATUS_OK;
    }
    if (count == 0)
    {
        *required = 0;
        return OPENUSD_STATUS_OK;
    }
    if (values == nullptr || capacity < count)
    {
        return OPENUSD_STATUS_BUFFER_TOO_SMALL;
    }
    for (size_t index = 0; index < count; ++index)
    {
        values[index] = convert(array[index]);
    }
    *required = count;
    return OPENUSD_STATUS_OK;
}

bool IsValidPrimPath(const char* value)
{
    if (value == nullptr)
    {
        return false;
    }
    const SdfPath path(value);
    return path.IsAbsolutePath() && path.IsPrimPath();
}

void FillStringList(
    openusd_string_list* result,
    const std::vector<std::string>& values,
    openusd_string_list_view* view)
{
    view->data = nullptr;
    view->data_size = 0;
    view->offsets = nullptr;
    view->offsets_size = 0;
    view->count = 0;
    for (const std::string& value : values)
    {
        if (value.find('\0') != std::string::npos)
        {
            throw std::invalid_argument("Packed string-list values must not contain embedded NULs.");
        }
        result->offsets.push_back(result->data.size());
        result->data.insert(result->data.end(), value.begin(), value.end());
        result->data.push_back('\0');
    }

    view->data = result->data.empty() ? nullptr : result->data.data();
    view->data_size = result->data.size();
    view->offsets = result->offsets.empty() ? nullptr : result->offsets.data();
    view->offsets_size = result->offsets.size() * sizeof(size_t);
    view->count = result->offsets.size();
    if (IsCompositionEnumerationFailpoint("string-list-after-fill"))
    {
        TF_RUNTIME_ERROR("Injected packed string-list diagnostic after fill.");
    }
}

void ResetStringListOutput(
    openusd_string_list** list,
    openusd_string_list_view* view) noexcept
{
    ResetAbiOutput(list);
    ResetVersionedAbiOutput(view);
}

void ResetPayloadArcListOutput(
    openusd_payload_arc_list** list,
    openusd_payload_arc_list_view* view) noexcept
{
    ResetAbiOutput(list);
    ResetVersionedAbiOutput(view);
    if (view == nullptr)
    {
        return;
    }

    uint32_t struct_size = 0;
    std::memcpy(&struct_size, view, sizeof(struct_size));
    if (struct_size >= offsetof(openusd_payload_arc_list_view, version) + sizeof(uint32_t))
    {
        const uint32_t version = OPENUSD_PAYLOAD_ARC_LIST_VIEW_VERSION;
        std::memcpy(
            reinterpret_cast<unsigned char*>(view) +
                offsetof(openusd_payload_arc_list_view, version),
            &version,
            sizeof(version));
    }
}

struct PayloadArcValue
{
    std::string asset_path;
    std::string target_prim_path;
    std::string source_layer_identifier;
};

void AppendPackedString(
    openusd_payload_arc_list* result,
    const std::string& value)
{
    if (value.find('\0') != std::string::npos)
    {
        throw std::invalid_argument("Packed payload-arc values must not contain embedded NULs.");
    }
    result->offsets.push_back(result->data.size());
    result->data.insert(result->data.end(), value.begin(), value.end());
    result->data.push_back('\0');
}

void FillPayloadArcList(
    openusd_payload_arc_list* result,
    const std::vector<PayloadArcValue>& values,
    openusd_payload_arc_list_view* view)
{
    view->version = OPENUSD_PAYLOAD_ARC_LIST_VIEW_VERSION;
    view->data = nullptr;
    view->data_size = 0;
    view->offsets = nullptr;
    view->offsets_size = 0;
    view->count = 0;
    if (values.size() >
        std::numeric_limits<size_t>::max() / (3 * sizeof(size_t)))
    {
        throw std::length_error("The payload-arc list is too large.");
    }
    result->offsets.reserve(values.size() * 3);
    for (const PayloadArcValue& value : values)
    {
        AppendPackedString(result, value.asset_path);
        AppendPackedString(result, value.target_prim_path);
        AppendPackedString(result, value.source_layer_identifier);
    }

    view->data = result->data.empty() ? nullptr : result->data.data();
    view->data_size = result->data.size();
    view->offsets = result->offsets.empty() ? nullptr : result->offsets.data();
    view->offsets_size = result->offsets.size() * sizeof(size_t);
    view->count = values.size();
    if (IsCompositionEnumerationFailpoint("payload-list-after-fill"))
    {
        TF_RUNTIME_ERROR("Injected packed payload-list diagnostic after fill.");
    }
}

template <typename TAction>
openusd_status GuardStringListOutput(
    openusd_error_buffer* error,
    openusd_string_list** list,
    openusd_string_list_view* view,
    TAction&& action)
{
    std::unique_ptr<openusd_string_list> result;
    openusd_status status = Guard(error, [&]()
    {
        return action(result);
    });
    if (status == OPENUSD_STATUS_OK && result)
    {
        *list = result.release();
        return OPENUSD_STATUS_OK;
    }
    if (status == OPENUSD_STATUS_OK)
    {
        WriteError(error, "The native operation did not produce a string-list owner.");
        status = OPENUSD_STATUS_NATIVE_ERROR;
    }
    result.reset();
    ResetStringListOutput(list, view);
    return status;
}

template <typename TAction>
openusd_status GuardPayloadArcListOutput(
    openusd_error_buffer* error,
    openusd_payload_arc_list** list,
    openusd_payload_arc_list_view* view,
    TAction&& action)
{
    std::unique_ptr<openusd_payload_arc_list> result;
    openusd_status status = Guard(error, [&]()
    {
        return action(result);
    });
    if (status == OPENUSD_STATUS_OK && result)
    {
        *list = result.release();
        return OPENUSD_STATUS_OK;
    }
    if (status == OPENUSD_STATUS_OK)
    {
        WriteError(error, "The native operation did not produce a payload-list owner.");
        status = OPENUSD_STATUS_NATIVE_ERROR;
    }
    result.reset();
    ResetPayloadArcListOutput(list, view);
    return status;
}

openusd_status ValidateStringListView(
    const openusd_string_list_view* view,
    const char* label,
    openusd_error_buffer* error)
{
    if (view == nullptr || view->struct_size < sizeof(openusd_string_list_view))
    {
        WriteError(error, std::string("A valid ABI v2 ") + label + " is required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (view->count > std::numeric_limits<size_t>::max() / sizeof(size_t) ||
        view->offsets_size != view->count * sizeof(size_t))
    {
        WriteError(error, std::string("The ") + label + " offset table size is invalid.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (view->count == 0)
    {
        if (view->data_size != 0 || view->data != nullptr || view->offsets != nullptr)
        {
            WriteError(error, std::string("The empty ") + label + " must not contain buffers.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return OPENUSD_STATUS_OK;
    }
    if (view->count > view->data_size || view->data == nullptr || view->offsets == nullptr ||
        !IsAligned(view->offsets))
    {
        WriteError(error, std::string("The ") + label + " buffers are invalid.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    size_t expected_offset = 0;
    for (size_t index = 0; index < view->count; ++index)
    {
        const size_t offset = view->offsets[index];
        if (offset != expected_offset || offset >= view->data_size)
        {
            WriteError(error, std::string("The ") + label + " contains an invalid entry.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const void* terminator =
            std::memchr(view->data + offset, '\0', view->data_size - offset);
        if (terminator == nullptr)
        {
            WriteError(error, std::string("The ") + label + " contains an invalid entry.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const auto* end = static_cast<const char*>(terminator);
        expected_offset = static_cast<size_t>(end - view->data) + 1;
    }
    if (expected_offset != view->data_size)
    {
        WriteError(error, std::string("The ") + label + " contains trailing or embedded data.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    return OPENUSD_STATUS_OK;
}

std::vector<SdfPath> ReadPathList(const openusd_string_list_view* targets)
{
    std::vector<SdfPath> paths;
    if (targets == nullptr || targets->count == 0)
    {
        return paths;
    }
    paths.reserve(targets->count);
    for (size_t i = 0; i < targets->count; ++i)
    {
        if (targets->offsets[i] >= targets->data_size)
        {
            throw std::invalid_argument("The target list contains an invalid offset.");
        }
        const char* start = targets->data + targets->offsets[i];
        const auto* end = static_cast<const char*>(
            std::memchr(start, '\0', targets->data_size - targets->offsets[i]));
        paths.emplace_back(SdfPath(std::string(start, end)));
    }
    return paths;
}

openusd_status ReadMetadataValue(
    const VtValue& stored,
    int32_t requested_kind,
    openusd_metadata_value* value,
    char* string_buffer,
    size_t string_capacity,
    size_t* string_required,
    openusd_error_buffer* error)
{
    if (string_required != nullptr)
    {
        *string_required = 0;
    }

    switch (requested_kind)
    {
    case OPENUSD_METADATA_KIND_STRING:
        if (!stored.IsHolding<std::string>())
        {
            WriteError(error, "The requested metadata is not a string.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        value->kind = OPENUSD_METADATA_KIND_STRING;
        return CopyString(
            stored.UncheckedGet<std::string>(), string_buffer, string_capacity, string_required);
    case OPENUSD_METADATA_KIND_BOOL:
        if (!stored.IsHolding<bool>())
        {
            WriteError(error, "The requested metadata is not a bool.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        value->kind = OPENUSD_METADATA_KIND_BOOL;
        value->bool_value = stored.UncheckedGet<bool>() ? 1 : 0;
        return OPENUSD_STATUS_OK;
    case OPENUSD_METADATA_KIND_INT64:
        if (!stored.IsHolding<int64_t>())
        {
            WriteError(error, "The requested metadata is not an int64.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        value->kind = OPENUSD_METADATA_KIND_INT64;
        value->int64_value = stored.UncheckedGet<int64_t>();
        return OPENUSD_STATUS_OK;
    case OPENUSD_METADATA_KIND_DOUBLE:
        if (!stored.IsHolding<double>())
        {
            WriteError(error, "The requested metadata is not a double.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        value->kind = OPENUSD_METADATA_KIND_DOUBLE;
        value->double_value = stored.UncheckedGet<double>();
        return OPENUSD_STATUS_OK;
    default:
        WriteError(error, "The requested metadata kind is not supported.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
}

VtValue MakeMetadataValue(const openusd_metadata_value* value, const char* string_value)
{
    switch (value->kind)
    {
    case OPENUSD_METADATA_KIND_STRING:
        return VtValue(std::string(string_value != nullptr ? string_value : ""));
    case OPENUSD_METADATA_KIND_BOOL:
        return VtValue(value->bool_value != 0);
    case OPENUSD_METADATA_KIND_INT64:
        return VtValue(static_cast<int64_t>(value->int64_value));
    case OPENUSD_METADATA_KIND_DOUBLE:
        return VtValue(value->double_value);
    default:
        throw std::invalid_argument("The supplied metadata kind is not supported.");
    }
}

SdfLayerHandle FindLayerInStack(const openusd_stage* stage, const std::string& identifier)
{
    for (const SdfLayerHandle& layer : stage->value->GetLayerStack(true))
    {
        if (layer && layer->GetIdentifier() == identifier)
        {
            return layer;
        }
    }
    return {};
}

bool IsKnownLayerIdentifier(const openusd_stage* stage, const std::string& identifier)
{
    if (FindLayerInStack(stage, identifier))
    {
        return true;
    }
    const std::vector<std::string>& mutedLayers = stage->value->GetMutedLayers();
    return std::find(mutedLayers.begin(), mutedLayers.end(), identifier) != mutedLayers.end();
}

openusd_status SetEditTargetLayer(
    const openusd_stage* stage,
    const SdfLayerHandle& layer,
    openusd_error_buffer* error)
{
    if (!layer)
    {
        WriteError(error, "A valid edit-target layer is required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    const SdfLayerHandleVector layerStack = stage->value->GetLayerStack(true);
    if (std::find(layerStack.begin(), layerStack.end(), layer) == layerStack.end())
    {
        WriteError(error, "The edit-target layer does not belong to the stage's local layer stack.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    TfErrorMark mark;
    const UsdEditTarget editTarget = stage->value->GetEditTargetForLocalLayer(layer);
    if (!editTarget.IsValid() || !mark.IsClean())
    {
        std::string message = ConsumeErrors(mark);
        WriteError(error, message.empty() ? "Could not create a local edit target." : message);
        return OPENUSD_STATUS_NATIVE_ERROR;
    }

    stage->value->SetEditTarget(editTarget);
    if (!mark.IsClean() || stage->value->GetEditTarget().GetLayer() != layer)
    {
        std::string message = ConsumeErrors(mark);
        WriteError(error, message.empty() ? "Could not set the stage edit target." : message);
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    return OPENUSD_STATUS_OK;
}

openusd_status ReadAbsolutePrimPaths(
    const openusd_string_list_view* view,
    std::vector<SdfPath>* paths,
    openusd_error_buffer* error)
{
    if (paths == nullptr)
    {
        WriteError(error, "A valid versioned prim-path list is required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    const openusd_status validation =
        ValidateStringListView(view, "prim-path list", error);
    if (validation != OPENUSD_STATUS_OK)
    {
        return validation;
    }
    if (view->count == 0)
    {
        paths->clear();
        return OPENUSD_STATUS_OK;
    }
    paths->clear();
    paths->reserve(view->count);
    for (size_t i = 0; i < view->count; ++i)
    {
        const size_t offset = view->offsets[i];
        if (offset >= view->data_size)
        {
            WriteError(error, "The prim-path list contains an invalid offset.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        const char* start = view->data + offset;
        const size_t remaining = view->data_size - offset;
        const void* terminator = std::memchr(start, '\0', remaining);
        if (terminator == nullptr)
        {
            WriteError(error, "The prim-path list contains an unterminated path.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        const auto* end = static_cast<const char*>(terminator);
        const SdfPath path(std::string(start, end));
        if (!path.IsAbsolutePath() || !path.IsPrimPath())
        {
            WriteError(error, "Population-mask paths must be absolute prim paths.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        paths->push_back(path);
    }
    return OPENUSD_STATUS_OK;
}

SdfValueTypeName GetShadeValueType(openusd_shade_value_type value)
{
    switch (value)
    {
        case OPENUSD_SHADE_VALUE_FLOAT:
            return SdfValueTypeNames->Float;
        case OPENUSD_SHADE_VALUE_COLOR3F:
            return SdfValueTypeNames->Color3f;
        case OPENUSD_SHADE_VALUE_VECTOR3F:
            return SdfValueTypeNames->Vector3f;
        case OPENUSD_SHADE_VALUE_NORMAL3F:
            return SdfValueTypeNames->Normal3f;
        case OPENUSD_SHADE_VALUE_TOKEN:
            return SdfValueTypeNames->Token;
        case OPENUSD_SHADE_VALUE_STRING:
            return SdfValueTypeNames->String;
        case OPENUSD_SHADE_VALUE_ASSET:
            return SdfValueTypeNames->Asset;
        case OPENUSD_SHADE_VALUE_FLOAT3:
            return SdfValueTypeNames->Float3;
        default:
            return {};
    }
}

openusd_shade_value_type GetShadeValueType(const SdfValueTypeName& value)
{
    if (value == SdfValueTypeNames->Float)
    {
        return OPENUSD_SHADE_VALUE_FLOAT;
    }
    if (value == SdfValueTypeNames->Color3f)
    {
        return OPENUSD_SHADE_VALUE_COLOR3F;
    }
    if (value == SdfValueTypeNames->Vector3f)
    {
        return OPENUSD_SHADE_VALUE_VECTOR3F;
    }
    if (value == SdfValueTypeNames->Normal3f)
    {
        return OPENUSD_SHADE_VALUE_NORMAL3F;
    }
    if (value == SdfValueTypeNames->Token)
    {
        return OPENUSD_SHADE_VALUE_TOKEN;
    }
    if (value == SdfValueTypeNames->String)
    {
        return OPENUSD_SHADE_VALUE_STRING;
    }
    if (value == SdfValueTypeNames->Asset)
    {
        return OPENUSD_SHADE_VALUE_ASSET;
    }
    if (value == SdfValueTypeNames->Float3)
    {
        return OPENUSD_SHADE_VALUE_FLOAT3;
    }
    return OPENUSD_SHADE_VALUE_INVALID;
}

bool AreShadeConnectionTypesCompatible(
    const SdfValueTypeName& source,
    const SdfValueTypeName& destination)
{
    return source == destination ||
        (source == SdfValueTypeNames->Float3 &&
         destination == SdfValueTypeNames->Color3f);
}

UsdPrim GetRequiredPrim(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error)
{
    if (!IsValidPrimPath(prim_path))
    {
        WriteError(error, "An absolute prim path is required.");
        return {};
    }
    const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
    if (!prim)
    {
        WriteError(error, "The requested prim does not exist.");
    }
    return prim;
}

UsdShadeConnectableAPI GetRequiredConnectable(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error)
{
    const UsdPrim prim = GetRequiredPrim(stage, prim_path, error);
    if (!prim)
    {
        return UsdShadeConnectableAPI(UsdPrim());
    }
    const UsdShadeConnectableAPI connectable(prim);
    if (!connectable)
    {
        WriteError(error, "The requested prim is not a UsdShade connectable schema.");
    }
    return connectable;
}

UsdShadeInput GetRequiredShadeInput(
    const openusd_stage* stage,
    const char* prim_path,
    const char* input_name,
    openusd_error_buffer* error)
{
    if (input_name == nullptr || input_name[0] == '\0')
    {
        WriteError(error, "A non-empty shader input name is required.");
        return {};
    }
    const UsdShadeConnectableAPI connectable =
        GetRequiredConnectable(stage, prim_path, error);
    if (!connectable)
    {
        return {};
    }
    const UsdShadeInput input = connectable.GetInput(TfToken(input_name));
    if (!input)
    {
        WriteError(error, "The requested shader input does not exist.");
    }
    return input;
}

UsdShadeOutput GetRequiredShadeOutput(
    const openusd_stage* stage,
    const char* prim_path,
    const char* output_name,
    openusd_error_buffer* error)
{
    if (output_name == nullptr || output_name[0] == '\0')
    {
        WriteError(error, "A non-empty shader output name is required.");
        return {};
    }
    const UsdShadeConnectableAPI connectable =
        GetRequiredConnectable(stage, prim_path, error);
    if (!connectable)
    {
        return {};
    }
    const UsdShadeOutput output = connectable.GetOutput(TfToken(output_name));
    if (!output)
    {
        WriteError(error, "The requested shader output does not exist.");
    }
    return output;
}

openusd_status ValidateShadeInputType(
    const UsdShadeInput& input,
    openusd_shade_value_type expected,
    openusd_error_buffer* error)
{
    const SdfValueTypeName type = GetShadeValueType(expected);
    if (!type)
    {
        WriteError(error, "The shader value type is unsupported.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (input.GetTypeName() != type)
    {
        WriteError(error, "The shader input type does not match the requested value type.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    return OPENUSD_STATUS_OK;
}

UsdShadeAttributeType GetShadeAttributeType(openusd_shade_attribute_type value)
{
    switch (value)
    {
        case OPENUSD_SHADE_ATTRIBUTE_INPUT:
            return UsdShadeAttributeType::Input;
        case OPENUSD_SHADE_ATTRIBUTE_OUTPUT:
            return UsdShadeAttributeType::Output;
        default:
            return UsdShadeAttributeType::Invalid;
    }
}

openusd_shade_attribute_type GetShadeAttributeType(UsdShadeAttributeType value)
{
    switch (value)
    {
        case UsdShadeAttributeType::Input:
            return OPENUSD_SHADE_ATTRIBUTE_INPUT;
        case UsdShadeAttributeType::Output:
            return OPENUSD_SHADE_ATTRIBUTE_OUTPUT;
        default:
            return OPENUSD_SHADE_ATTRIBUTE_INVALID;
    }
}

template <typename TValue>
openusd_status SetShadeInputValue(
    const openusd_stage* stage,
    const char* shader_path,
    const char* input_name,
    openusd_shade_value_type expected_type,
    const TValue& value,
    openusd_error_buffer* error)
{
    const UsdShadeInput input =
        GetRequiredShadeInput(stage, shader_path, input_name, error);
    if (!input)
    {
        return IsValidPrimPath(shader_path)
            ? OPENUSD_STATUS_NOT_FOUND
            : OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    const openusd_status validation =
        ValidateShadeInputType(input, expected_type, error);
    if (validation != OPENUSD_STATUS_OK)
    {
        return validation;
    }
    if (!input.Set(value))
    {
        WriteError(error, "Could not author the shader input value.");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    return OPENUSD_STATUS_OK;
}

template <typename TValue>
openusd_status GetShadeInputValue(
    const openusd_stage* stage,
    const char* shader_path,
    const char* input_name,
    openusd_shade_value_type expected_type,
    TValue* value,
    openusd_error_buffer* error)
{
    const UsdShadeInput input =
        GetRequiredShadeInput(stage, shader_path, input_name, error);
    if (!input)
    {
        return IsValidPrimPath(shader_path)
            ? OPENUSD_STATUS_NOT_FOUND
            : OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    const openusd_status validation =
        ValidateShadeInputType(input, expected_type, error);
    if (validation != OPENUSD_STATUS_OK)
    {
        return validation;
    }
    if (!input.Get(value))
    {
        WriteError(error, "The shader input has no readable value.");
        return OPENUSD_STATUS_NOT_FOUND;
    }
    return OPENUSD_STATUS_OK;
}

template <typename TShadeProperty>
openusd_status GetConnectedShadeSource(
    const TShadeProperty& property,
    std::unique_ptr<openusd_string_list>& result,
    openusd_string_list_view* view,
    openusd_shade_attribute_type* source_type,
    openusd_error_buffer* error)
{
    const UsdShadeSourceInfoVector sources = property.GetConnectedSources();
    if (sources.empty())
    {
        WriteError(error, "The shading property has no connected source.");
        return OPENUSD_STATUS_NOT_FOUND;
    }
    if (sources.size() != 1)
    {
        WriteError(error, "The shading property does not have exactly one connected source.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    const UsdShadeConnectionSourceInfo& source = sources.front();
    if (!source.source || source.sourceName.IsEmpty())
    {
        WriteError(error, "The shading property connection is invalid.");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }

    std::vector<std::string> values{
        source.source.GetPrim().GetPath().GetString(),
        source.sourceName.GetString()
    };
    result = std::make_unique<openusd_string_list>();
    FillStringList(result.get(), values, view);
    *source_type = GetShadeAttributeType(source.sourceType);
    return OPENUSD_STATUS_OK;
}

template <typename TShadeProperty>
openusd_status GetConnectedShadeSources(
    const TShadeProperty& property,
    std::unique_ptr<openusd_string_list>& result,
    openusd_string_list_view* view,
    openusd_error_buffer* error)
{
    const UsdShadeSourceInfoVector sources = property.GetConnectedSources();
    if (sources.empty())
    {
        WriteError(error, "The shading property has no connected source.");
        return OPENUSD_STATUS_NOT_FOUND;
    }

    std::vector<std::string> values;
    values.reserve(sources.size() * 3);
    for (const UsdShadeConnectionSourceInfo& source : sources)
    {
        const openusd_shade_attribute_type type = GetShadeAttributeType(source.sourceType);
        if (!source.source || source.sourceName.IsEmpty() ||
            type == OPENUSD_SHADE_ATTRIBUTE_INVALID)
        {
            WriteError(error, "The shading property contains an invalid connection.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        values.push_back(source.source.GetPrim().GetPath().GetString());
        values.push_back(source.sourceName.GetString());
        values.push_back(std::to_string(static_cast<int32_t>(type)));
    }

    result = std::make_unique<openusd_string_list>();
    FillStringList(result.get(), values, view);
    return OPENUSD_STATUS_OK;
}

template <typename TValue>
openusd_status SetLuxAttribute(
    const UsdAttribute& attribute,
    const TValue& value,
    const char* label,
    openusd_error_buffer* error)
{
    TfErrorMark mark;
    const bool set = attribute && attribute.Set(value);
    if (!set || !mark.IsClean())
    {
        std::string message = ConsumeErrors(mark);
        WriteError(error, message.empty() ? std::string("Could not set ") + label + "." : message);
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    return OPENUSD_STATUS_OK;
}

template <typename TValue>
openusd_status GetLuxAttribute(
    const UsdAttribute& attribute,
    TValue* value,
    const char* label,
    openusd_error_buffer* error)
{
    TfErrorMark mark;
    const bool read = attribute && attribute.Get(value);
    if (!read || !mark.IsClean())
    {
        const bool had_errors = !mark.IsClean();
        std::string message = ConsumeErrors(mark);
        WriteError(error, message.empty() ? std::string("Could not read ") + label + "." : message);
        return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
    }
    return OPENUSD_STATUS_OK;
}

bool IsLuxSchema(const UsdPrim& prim, openusd_lux_schema_kind schema_kind)
{
    switch (schema_kind)
    {
        case OPENUSD_LUX_SCHEMA_DISTANT_LIGHT:
            return prim.IsA<UsdLuxDistantLight>();
        case OPENUSD_LUX_SCHEMA_SPHERE_LIGHT:
            return prim.IsA<UsdLuxSphereLight>();
        case OPENUSD_LUX_SCHEMA_RECT_LIGHT:
            return prim.IsA<UsdLuxRectLight>();
        case OPENUSD_LUX_SCHEMA_DISK_LIGHT:
            return prim.IsA<UsdLuxDiskLight>();
        case OPENUSD_LUX_SCHEMA_DOME_LIGHT:
            return prim.IsA<UsdLuxDomeLight>();
        case OPENUSD_LUX_SCHEMA_CYLINDER_LIGHT:
            return prim.IsA<UsdLuxCylinderLight>();
        default:
            return false;
    }
}

openusd_status GetLuxLight(
    const openusd_stage* stage,
    const char* prim_path,
    UsdPrim* prim,
    UsdLuxLightAPI* light,
    openusd_error_buffer* error)
{
    if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
        prim == nullptr || light == nullptr)
    {
        WriteError(error, "A valid stage, absolute light path, and outputs are required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    *prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
    if (!*prim)
    {
        WriteError(error, "The requested light prim does not exist.");
        return OPENUSD_STATUS_NOT_FOUND;
    }
    *light = UsdLuxLightAPI(*prim);
    if (!*light)
    {
        WriteError(error, "The requested prim does not provide UsdLuxLightAPI.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    return OPENUSD_STATUS_OK;
}

openusd_status GetLuxLight(
    const openusd_stage* stage,
    const char* prim_path,
    UsdLuxLightAPI* light,
    openusd_error_buffer* error)
{
    UsdPrim prim;
    return GetLuxLight(stage, prim_path, &prim, light, error);
}

UsdAttribute GetLuxShapeAttribute(
    const UsdPrim& prim,
    openusd_lux_shape_property property,
    bool create,
    openusd_error_buffer* error)
{
    switch (property)
    {
        case OPENUSD_LUX_SHAPE_ANGLE:
        {
            const UsdLuxDistantLight light(prim);
            if (light)
            {
                return create ? light.CreateAngleAttr() : light.GetAngleAttr();
            }
            break;
        }
        case OPENUSD_LUX_SHAPE_RADIUS:
        {
            const UsdLuxSphereLight sphere(prim);
            if (sphere)
            {
                return create ? sphere.CreateRadiusAttr() : sphere.GetRadiusAttr();
            }
            const UsdLuxDiskLight disk(prim);
            if (disk)
            {
                return create ? disk.CreateRadiusAttr() : disk.GetRadiusAttr();
            }
            const UsdLuxCylinderLight cylinder(prim);
            if (cylinder)
            {
                return create ? cylinder.CreateRadiusAttr() : cylinder.GetRadiusAttr();
            }
            break;
        }
        case OPENUSD_LUX_SHAPE_WIDTH:
        {
            const UsdLuxRectLight light(prim);
            if (light)
            {
                return create ? light.CreateWidthAttr() : light.GetWidthAttr();
            }
            break;
        }
        case OPENUSD_LUX_SHAPE_HEIGHT:
        {
            const UsdLuxRectLight light(prim);
            if (light)
            {
                return create ? light.CreateHeightAttr() : light.GetHeightAttr();
            }
            break;
        }
        case OPENUSD_LUX_SHAPE_LENGTH:
        {
            const UsdLuxCylinderLight light(prim);
            if (light)
            {
                return create ? light.CreateLengthAttr() : light.GetLengthAttr();
            }
            break;
        }
        default:
            WriteError(error, "The requested light shape property is unsupported.");
            return {};
    }
    WriteError(error, "The light schema does not support the requested shape property.");
    return {};
}

UsdAttribute GetLuxTextureAttribute(
    const UsdPrim& prim,
    openusd_lux_asset_property property,
    bool create,
    openusd_error_buffer* error)
{
    if (property != OPENUSD_LUX_ASSET_TEXTURE_FILE)
    {
        WriteError(error, "The requested light asset property is unsupported.");
        return {};
    }
    const UsdLuxRectLight rect(prim);
    if (rect)
    {
        return create ? rect.CreateTextureFileAttr() : rect.GetTextureFileAttr();
    }
    const UsdLuxDomeLight dome(prim);
    if (dome)
    {
        return create ? dome.CreateTextureFileAttr() : dome.GetTextureFileAttr();
    }
    WriteError(error, "The light schema does not support a texture file.");
    return {};
}

UsdAttribute GetLuxShapingAttribute(
    const UsdLuxShapingAPI& shaping,
    openusd_lux_shaping_property property,
    bool create,
    openusd_error_buffer* error)
{
    switch (property)
    {
        case OPENUSD_LUX_SHAPING_FOCUS:
            return create
                ? shaping.CreateShapingFocusAttr()
                : shaping.GetShapingFocusAttr();
        case OPENUSD_LUX_SHAPING_CONE_ANGLE:
            return create
                ? shaping.CreateShapingConeAngleAttr()
                : shaping.GetShapingConeAngleAttr();
        case OPENUSD_LUX_SHAPING_CONE_SOFTNESS:
            return create
                ? shaping.CreateShapingConeSoftnessAttr()
                : shaping.GetShapingConeSoftnessAttr();
        default:
            WriteError(error, "The requested shaping property is unsupported.");
            return {};
    }
}

GfMatrix4d ToMatrix4d(const openusd_matrix4d& value)
{
    GfMatrix4d matrix(0.0);
    for (int row = 0; row < 4; ++row)
    {
        for (int column = 0; column < 4; ++column)
        {
            matrix[row][column] = value.values[(row * 4) + column];
        }
    }
    return matrix;
}

openusd_matrix4d FromMatrix4d(const GfMatrix4d& value)
{
    openusd_matrix4d matrix{};
    for (int row = 0; row < 4; ++row)
    {
        for (int column = 0; column < 4; ++column)
        {
            matrix.values[(row * 4) + column] = value[row][column];
        }
    }
    return matrix;
}

bool IsFiniteMatrix(const openusd_matrix4d& value)
{
    return std::all_of(
        std::begin(value.values),
        std::end(value.values),
        [](double component) { return std::isfinite(component); });
}

openusd_status ReadSkelTokens(
    const openusd_string_list_view* view,
    bool require_topology,
    VtTokenArray* tokens,
    openusd_error_buffer* error)
{
    if (tokens == nullptr)
    {
        WriteError(error, "A valid versioned joint-token list is required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    const openusd_status validation =
        ValidateStringListView(view, "joint-token list", error);
    if (validation != OPENUSD_STATUS_OK)
    {
        return validation;
    }

    tokens->clear();
    tokens->reserve(view->count);
    std::unordered_set<std::string> unique;
    for (size_t index = 0; index < view->count; ++index)
    {
        const size_t offset = view->offsets[index];
        if (offset >= view->data_size)
        {
            WriteError(error, "The joint-token list contains an invalid offset.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const char* start = view->data + offset;
        const size_t remaining = view->data_size - offset;
        const void* terminator = std::memchr(start, '\0', remaining);
        if (terminator == nullptr)
        {
            WriteError(error, "The joint-token list contains an unterminated token.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const auto* end = static_cast<const char*>(terminator);
        std::string text(start, end);
        const SdfPath path(text);
        if (text.empty() || !path.IsPrimPath() || path.IsAbsolutePath())
        {
            WriteError(error, "Joint tokens must be unique relative prim paths.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (!unique.insert(text).second)
        {
            WriteError(error, "Joint tokens must be unique.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        tokens->push_back(TfToken(text));
    }

    if (require_topology)
    {
        const UsdSkelTopology topology(
            TfSpan<const TfToken>(tokens->data(), tokens->size()));
        std::string reason;
        if (!topology.Validate(&reason))
        {
            WriteError(
                error,
                reason.empty() ? "The skeleton joint ordering is invalid." : reason);
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
    }
    return OPENUSD_STATUS_OK;
}

std::vector<std::string> ToStrings(const VtTokenArray& tokens)
{
    std::vector<std::string> values;
    values.reserve(tokens.size());
    for (const TfToken& token : tokens)
    {
        values.push_back(token.GetString());
    }
    return values;
}

bool IsSkelSchema(const UsdPrim& prim, openusd_skel_schema_kind schema_kind)
{
    switch (schema_kind)
    {
        case OPENUSD_SKEL_SCHEMA_ROOT:
            return prim.IsA<UsdSkelRoot>();
        case OPENUSD_SKEL_SCHEMA_SKELETON:
            return prim.IsA<UsdSkelSkeleton>();
        case OPENUSD_SKEL_SCHEMA_ANIMATION:
            return prim.IsA<UsdSkelAnimation>();
        default:
            return false;
    }
}

openusd_status GetSkelPrim(
    const openusd_stage* stage,
    const char* prim_path,
    UsdPrim* prim,
    openusd_error_buffer* error)
{
    if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) || prim == nullptr)
    {
        WriteError(error, "A valid stage, absolute prim path, and prim output are required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    *prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
    if (!*prim)
    {
        WriteError(error, std::string("Prim was not found: ") + prim_path);
        return OPENUSD_STATUS_NOT_FOUND;
    }
    return OPENUSD_STATUS_OK;
}

openusd_status GetSkelSkeleton(
    const openusd_stage* stage,
    const char* prim_path,
    UsdSkelSkeleton* skeleton,
    openusd_error_buffer* error)
{
    UsdPrim prim;
    const openusd_status status = GetSkelPrim(stage, prim_path, &prim, error);
    if (status != OPENUSD_STATUS_OK)
    {
        return status;
    }
    *skeleton = UsdSkelSkeleton(prim);
    if (!*skeleton)
    {
        WriteError(error, "The requested prim is not a UsdSkelSkeleton.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    return OPENUSD_STATUS_OK;
}

openusd_status GetSkelAnimation(
    const openusd_stage* stage,
    const char* prim_path,
    UsdSkelAnimation* animation,
    openusd_error_buffer* error)
{
    UsdPrim prim;
    const openusd_status status = GetSkelPrim(stage, prim_path, &prim, error);
    if (status != OPENUSD_STATUS_OK)
    {
        return status;
    }
    *animation = UsdSkelAnimation(prim);
    if (!*animation)
    {
        WriteError(error, "The requested prim is not a UsdSkelAnimation.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    return OPENUSD_STATUS_OK;
}

openusd_status GetSkelBinding(
    const openusd_stage* stage,
    const char* prim_path,
    UsdPrim* prim,
    UsdSkelBindingAPI* binding,
    openusd_error_buffer* error)
{
    const openusd_status status = GetSkelPrim(stage, prim_path, prim, error);
    if (status != OPENUSD_STATUS_OK)
    {
        return status;
    }
    *binding = UsdSkelBindingAPI(*prim);
    if (!*binding)
    {
        WriteError(error, "UsdSkelBindingAPI is not applied to the requested prim.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    return OPENUSD_STATUS_OK;
}

template <typename TValue>
openusd_status ValidateAuthoredArrayCardinality(
    const UsdAttribute& attribute,
    size_t expected,
    const char* label,
    openusd_error_buffer* error)
{
    if (!attribute || !attribute.HasAuthoredValueOpinion())
    {
        return OPENUSD_STATUS_OK;
    }

    VtArray<TValue> values;
    if (attribute.Get(&values, UsdTimeCode::Default()) && values.size() != expected)
    {
        WriteError(error, std::string(label) + " cardinality does not match joints.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    std::vector<double> samples;
    attribute.GetTimeSamples(&samples);
    for (double sample : samples)
    {
        values.clear();
        if (attribute.Get(&values, UsdTimeCode(sample)) && values.size() != expected)
        {
            WriteError(error, std::string(label) + " sample cardinality does not match joints.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
    }
    return OPENUSD_STATUS_OK;
}

openusd_status GetSkeletonJointCount(
    const UsdSkelSkeleton& skeleton,
    size_t* count,
    openusd_error_buffer* error)
{
    VtTokenArray joints;
    if (!skeleton.GetJointsAttr().Get(&joints))
    {
        WriteError(error, "The skeleton has no authored joints.");
        return OPENUSD_STATUS_NOT_FOUND;
    }
    *count = joints.size();
    return OPENUSD_STATUS_OK;
}

openusd_status GetAnimationJointCount(
    const UsdSkelAnimation& animation,
    size_t* count,
    openusd_error_buffer* error)
{
    VtTokenArray joints;
    if (!animation.GetJointsAttr().Get(&joints))
    {
        WriteError(error, "The animation has no authored joints.");
        return OPENUSD_STATUS_NOT_FOUND;
    }
    *count = joints.size();
    return OPENUSD_STATUS_OK;
}

UsdRelationship GetSkelBindingRelationship(
    const UsdSkelBindingAPI& binding,
    openusd_skel_binding_relationship relationship,
    bool create,
    openusd_error_buffer* error)
{
    switch (relationship)
    {
        case OPENUSD_SKEL_BINDING_SKELETON:
            return create ? binding.CreateSkeletonRel() : binding.GetSkeletonRel();
        case OPENUSD_SKEL_BINDING_ANIMATION_SOURCE:
            return create ? binding.CreateAnimationSourceRel() : binding.GetAnimationSourceRel();
        default:
            WriteError(error, "The requested skeleton binding relationship is unsupported.");
            return {};
    }
}

openusd_status ValidateSkelBindingTarget(
    const openusd_stage* stage,
    openusd_skel_binding_relationship relationship,
    const SdfPath& target_path,
    openusd_error_buffer* error)
{
    const UsdPrim target = stage->value->GetPrimAtPath(target_path);
    if (!target)
    {
        WriteError(error, std::string("The skeleton binding target does not exist: ") +
            target_path.GetString());
        return OPENUSD_STATUS_NOT_FOUND;
    }
    const bool valid = relationship == OPENUSD_SKEL_BINDING_SKELETON
        ? static_cast<bool>(UsdSkelSkeleton(target))
        : relationship == OPENUSD_SKEL_BINDING_ANIMATION_SOURCE
            ? static_cast<bool>(UsdSkelAnimation(target))
            : false;
    if (!valid)
    {
        WriteError(error, "The skeleton binding target has the wrong schema.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    return OPENUSD_STATUS_OK;
}

openusd_status GetBoundSkeletonJointCount(
    const UsdSkelBindingAPI& binding,
    size_t* count,
    openusd_error_buffer* error)
{
    const UsdSkelSkeleton skeleton = binding.GetInheritedSkeleton();
    if (!skeleton)
    {
        WriteError(error, "No valid skeleton is bound at or above the prim.");
        return OPENUSD_STATUS_NOT_FOUND;
    }
    return GetSkeletonJointCount(skeleton, count, error);
}
}
