// Copyright (c) marcschier. Licensed under the MIT License.

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

std::atomic<size_t> DiagnosticLiveStageCoreCount{0};
std::atomic<size_t> DiagnosticPeakStageCoreCount{0};
#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
std::atomic<size_t> TestDestroyedStageCoreCount{0};
#endif

void UpdatePeak(std::atomic<size_t>& peak, size_t value) noexcept
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
constexpr uint32_t DataAbiVersion = 8;
constexpr uint64_t DataCapabilities =
    OPENUSD_CAPABILITY_STRING_LIST_V2 |
    OPENUSD_CAPABILITY_GUARDED_STATUS_EXPORTS |
    OPENUSD_CAPABILITY_SHADE_CONNECTED_SOURCES |
    OPENUSD_CAPABILITY_SHARED_STAGE_ACCESS |
    OPENUSD_CAPABILITY_WORLD_BOUNDS_QUERY |
    OPENUSD_CAPABILITY_VARIANT_SET_NAMES |
    OPENUSD_CAPABILITY_COMPOSED_DIRECT_PAYLOAD_ARCS |
    OPENUSD_CAPABILITY_WORLD_TRANSFORM_QUERY |
    OPENUSD_CAPABILITY_CAMERA_STATE_QUERY;
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

uint32_t openusd_get_abi_version(void)
{
    return DataAbiVersion;
}

uint64_t openusd_get_capabilities(void)
{
    return DataCapabilities;
}

openusd_status openusd_renderer_stage_initialize(
    const openusd_stage_access* access,
    openusd_renderer_stage_initializer initializer,
    void* renderer_context,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        if (access == nullptr || initializer == nullptr || !access->lock.owns_lock() ||
            access->owner != std::this_thread::get_id() || access->stage == nullptr ||
            !access->stage->value)
        {
            WriteError(error, "An owner-thread stage access guard and renderer initializer are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        UsdStageRefPtr stage_view = access->stage->value;
        return initializer(&stage_view, renderer_context, error);
    });
}

extern "C" OPENUSD_DOTNET_API size_t openusd_diagnostic_get_live_stage_core_count(void)
{
    return DiagnosticLiveStageCoreCount.load(std::memory_order_relaxed);
}

extern "C" OPENUSD_DOTNET_API size_t openusd_diagnostic_get_peak_stage_core_count(void)
{
    return DiagnosticPeakStageCoreCount.load(std::memory_order_relaxed);
}

extern "C" OPENUSD_DOTNET_API void openusd_diagnostic_reset_peak_stage_core_count(void)
{
    DiagnosticPeakStageCoreCount.store(
        DiagnosticLiveStageCoreCount.load(std::memory_order_relaxed),
        std::memory_order_relaxed);
}

extern "C" OPENUSD_DOTNET_API openusd_status openusd_diagnostic_set_display_color(
    openusd_stage* stage,
    const char* prim_path,
    float red,
    float green,
    float blue,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || prim_path == nullptr)
        {
            WriteError(error, "A stage and prim path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (!std::isfinite(red) || !std::isfinite(green) || !std::isfinite(blue))
        {
            WriteError(error, "Display color components must be finite.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        const SdfPath path(prim_path);
        if (!IsValidPrimPath(prim_path))
        {
            WriteError(error, "The prim path must be an absolute prim path.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        const UsdGeomGprim gprim(stage->value->GetPrimAtPath(path));
        if (!gprim)
        {
            WriteError(error, "The prim does not exist or is not a geometric prim.");
            return OPENUSD_STATUS_NOT_FOUND;
        }

        UsdGeomPrimvar display_color = gprim.GetDisplayColorPrimvar();
        if (!display_color)
        {
            display_color = gprim.CreateDisplayColorPrimvar();
        }
        VtArray<GfVec3f> colors(1);
        colors[0] = GfVec3f(red, green, blue);
        return display_color.Set(colors) ? OPENUSD_STATUS_OK : OPENUSD_STATUS_NATIVE_ERROR;
    });
}

#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
extern "C" OPENUSD_DOTNET_API size_t openusd_test_get_live_stage_core_count(void)
{
    return DiagnosticLiveStageCoreCount.load(std::memory_order_relaxed);
}

extern "C" OPENUSD_DOTNET_API size_t openusd_test_get_destroyed_stage_core_count(void)
{
    return TestDestroyedStageCoreCount.load(std::memory_order_relaxed);
}
#endif

openusd_status openusd_get_version(
    char* buffer,
    size_t capacity,
    size_t* required)
{
    // OUTER_ABI_GUARD
    return Guard(nullptr, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiStringOutput(buffer, capacity);
        ResetAbiOutput(required);
        const uint32_t major = PXR_VERSION / 100;
        const uint32_t minor = PXR_VERSION % 100;
        char version[16];
        const int length = std::snprintf(version, sizeof(version), "%u.%02u", major, minor);
        if (length < 0 || static_cast<size_t>(length) >= sizeof(version))
        {
            return OPENUSD_STATUS_NATIVE_ERROR;
        }

        if (required == nullptr)
        {
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        *required = static_cast<size_t>(length) + 1;
        if (buffer == nullptr || capacity < *required)
        {
            return OPENUSD_STATUS_BUFFER_TOO_SMALL;
        }

        std::memcpy(buffer, version, *required);
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_register_plugins(
    const char* path,
    size_t* plugin_count,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(plugin_count);
        if (path == nullptr || plugin_count == nullptr)
        {
            WriteError(error, "Plugin path and count are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const PlugPluginPtrVector plugins = PlugRegistry::GetInstance().RegisterPlugins(path);
            if (!mark.IsClean())
            {
                WriteError(error, ConsumeErrors(mark));
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            *plugin_count = plugins.size();
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_decode_image_rgba8(
    const char* asset_path,
    uint32_t convert_srgb_to_linear,
    openusd_image_info* info,
    uint8_t* rgba,
    size_t rgba_size,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        if (asset_path == nullptr || info == nullptr ||
            info->struct_size != sizeof(openusd_image_info) ||
            info->version != OPENUSD_IMAGE_INFO_VERSION)
        {
            WriteError(error, "A valid image asset path and image-info output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            HioImageSharedPtr image = HioImage::OpenForReading(
                asset_path,
                0,
                0,
                HioImage::SourceColorSpace::Raw);
            if (!image)
            {
                WriteError(error, std::string("Could not open image: ") + asset_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (!mark.IsClean())
            {
                WriteError(error, ConsumeErrors(mark));
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            const int width = image->GetWidth();
            const int height = image->GetHeight();
            if (width <= 0 || height <= 0)
            {
                WriteError(error, "Image dimensions are invalid.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            info->width = static_cast<uint32_t>(width);
            info->height = static_cast<uint32_t>(height);
            const size_t required =
                static_cast<size_t>(width) * static_cast<size_t>(height) * 4u;
            if (rgba == nullptr || rgba_size < required)
            {
                return OPENUSD_STATUS_BUFFER_TOO_SMALL;
            }

            HioImage::StorageSpec storage;
            storage.width = width;
            storage.height = height;
            storage.depth = 1;
            storage.format = HioFormatUNorm8Vec4;
            storage.flipped = false;
            storage.data = rgba;
            if (!image->Read(storage))
            {
                WriteError(error, std::string("Could not read image: ") + asset_path);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            if (convert_srgb_to_linear != 0)
            {
                for (size_t index = 0; index < required; index += 4)
                {
                    for (size_t component = 0; component < 3; ++component)
                    {
                        const double srgb = static_cast<double>(rgba[index + component]) / 255.0;
                        const double linear = srgb <= 0.04045
                            ? srgb / 12.92
                            : std::pow((srgb + 0.055) / 1.055, 2.4);
                        const long rounded = std::lround(std::max(0.0, std::min(1.0, linear)) * 255.0);
                        rgba[index + component] = static_cast<uint8_t>(rounded);
                    }
                }
            }
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_stage_open(
    const char* path,
    openusd_stage** stage,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(stage);
        if (path == nullptr || stage == nullptr)
        {
            WriteError(error, "Stage path and output handle are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        *stage = nullptr;
        return Guard(error, [&]()
        {
            TfErrorMark mark;
            UsdStageRefPtr value = UsdStage::Open(path);
            if (!value || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                if (message.empty())
                {
                    message = std::string("Could not open stage: ") + path;
                }
                WriteError(error, message);
                return value ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }

            auto handle = std::make_unique<openusd_stage>(std::move(value));
            *stage = handle.release();
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_open_masked(
    const char* path,
    const openusd_string_list_view* mask_paths,
    openusd_stage** stage,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(stage);
        if (path == nullptr || path[0] == '\0' || stage == nullptr)
        {
            WriteError(error, "Stage path, population mask, and output handle are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        *stage = nullptr;
        std::vector<SdfPath> paths;
        const openusd_status pathStatus = ReadAbsolutePrimPaths(mask_paths, &paths, error);
        if (pathStatus != OPENUSD_STATUS_OK)
        {
            return pathStatus;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const UsdStagePopulationMask mask(std::move(paths));
            UsdStageRefPtr value = UsdStage::OpenMasked(path, mask);
            if (!value || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                if (message.empty())
                {
                    message = std::string("Could not open masked stage: ") + path;
                }
                WriteError(error, message);
                return value ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }

            auto handle = std::make_unique<openusd_stage>(std::move(value));
            *stage = handle.release();
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_create_new(
    const char* path,
    openusd_stage** stage,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(stage);
        if (path == nullptr || stage == nullptr)
        {
            WriteError(error, "Stage path and output handle are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        *stage = nullptr;
        return Guard(error, [&]()
        {
            TfErrorMark mark;
            UsdStageRefPtr value = UsdStage::CreateNew(path);
            if (!value || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                if (message.empty())
                {
                    message = std::string("Could not create stage: ") + path;
                }
                WriteError(error, message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }

            auto handle = std::make_unique<openusd_stage>(std::move(value));
            *stage = handle.release();
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_retain(
    openusd_stage* stage,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!RetainStageReference(stage))
        {
            WriteError(error, "A live stage handle is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return OPENUSD_STATUS_OK;
    });
}

void openusd_stage_release(openusd_stage* stage)
{
    ReleaseStageReference(stage);
}

openusd_status openusd_stage_access_begin(
    openusd_stage* stage,
    openusd_stage_access** access,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(access);
        if (access == nullptr || !RetainStageReference(stage))
        {
            WriteError(error, "A live stage and access output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        try
        {
            if (IsStageAccessBeginFailpoint("after-retain"))
            {
                throw std::bad_alloc();
            }
            auto guard = std::make_unique<openusd_stage_access>(stage);
            if (IsStageAccessBeginFailpoint("after-lock"))
            {
                throw std::runtime_error("Injected stage access begin failure after locking.");
            }
            *access = guard.release();
            return OPENUSD_STATUS_OK;
        }
        catch (...)
        {
            ReleaseStageReference(stage);
            throw;
        }
    });
}

openusd_status openusd_stage_access_end(
    openusd_stage_access* access,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    const openusd_status validation = Guard(error, [&]() -> openusd_status
    {
        if (access == nullptr || !access->lock.owns_lock())
        {
            WriteError(error, "A live stage access guard is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (access->owner != std::this_thread::get_id())
        {
            WriteError(error, "The stage access guard must end on its owner thread.");
            return OPENUSD_STATUS_WRONG_THREAD;
        }
        return OPENUSD_STATUS_OK;
    });
    if (validation != OPENUSD_STATUS_OK)
    {
        return validation;
    }

    FinalizeStageAccess(access);
    return OPENUSD_STATUS_OK;
}

openusd_status openusd_stage_get_root_layer(
    const openusd_stage* stage,
    openusd_layer** layer,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(layer);
        if (stage == nullptr || !stage->value || layer == nullptr)
        {
            WriteError(error, "A valid stage and layer output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        *layer = nullptr;
        return Guard(error, [&]()
        {
            auto handle = std::make_unique<openusd_layer>();
            handle->value = stage->value->GetRootLayer();
            if (!handle->value)
            {
                WriteError(error, "The stage has no root layer.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            if (!RetainStageReference(const_cast<openusd_stage*>(stage)))
            {
                WriteError(error, "The stage could not be retained for the root layer.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            handle->stage = const_cast<openusd_stage*>(stage);
            *layer = handle.release();
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_session_layer(
    const openusd_stage* stage,
    openusd_layer** layer,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(layer);
        if (stage == nullptr || !stage->value || layer == nullptr)
        {
            WriteError(error, "A valid stage and layer output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        *layer = nullptr;
        return Guard(error, [&]()
        {
            auto handle = std::make_unique<openusd_layer>();
            handle->value = stage->value->GetSessionLayer();
            if (!handle->value)
            {
                WriteError(error, "The stage has no session layer.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (!RetainStageReference(const_cast<openusd_stage*>(stage)))
            {
                WriteError(error, "The stage could not be retained for the session layer.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            handle->stage = const_cast<openusd_stage*>(stage);
            *layer = handle.release();
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_root_layer_identifier(
    const openusd_stage* stage,
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
        if (stage == nullptr || !stage->value)
        {
            WriteError(error, "A valid stage handle is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            return CopyString(stage->value->GetRootLayer()->GetIdentifier(), buffer, capacity, required);
        });

    });
}

openusd_status openusd_stage_get_session_layer_identifier(
    const openusd_stage* stage,
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
        if (stage == nullptr || !stage->value)
        {
            WriteError(error, "A valid stage handle is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const SdfLayerHandle layer = stage->value->GetSessionLayer();
            if (!layer)
            {
                WriteError(error, "The stage has no session layer.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            return CopyString(layer->GetIdentifier(), buffer, capacity, required);
        });

    });
}

openusd_status openusd_stage_get_edit_target_layer_identifier(
    const openusd_stage* stage,
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
        if (stage == nullptr || !stage->value)
        {
            WriteError(error, "A valid stage handle is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const SdfLayerHandle layer = stage->value->GetEditTarget().GetLayer();
            if (!layer)
            {
                WriteError(error, "The stage has no valid edit-target layer.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            return CopyString(layer->GetIdentifier(), buffer, capacity, required);
        });

    });
}

openusd_status openusd_stage_set_edit_target_root_layer(
    const openusd_stage* stage,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value)
        {
            WriteError(error, "A valid stage handle is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            return SetEditTargetLayer(stage, stage->value->GetRootLayer(), error);
        });

    });
}

openusd_status openusd_stage_set_edit_target_session_layer(
    const openusd_stage* stage,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value)
        {
            WriteError(error, "A valid stage handle is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            const SdfLayerHandle layer = stage->value->GetSessionLayer();
            if (!layer)
            {
                WriteError(error, "The stage has no session layer.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            return SetEditTargetLayer(stage, layer, error);
        });

    });
}

openusd_status openusd_stage_set_edit_target_layer(
    const openusd_stage* stage,
    const openusd_layer* layer,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || layer == nullptr || !layer->value ||
            layer->stage != stage)
        {
            WriteError(error, "A valid stage and owned layer handle are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            return SetEditTargetLayer(stage, layer->value, error);
        });

    });
}

openusd_status openusd_stage_get_layer_stack_identifiers(
    const openusd_stage* stage,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetStringListOutput(list, view);
        if (stage == nullptr || !stage->value || list == nullptr || view == nullptr ||
            view->struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(error, "A valid stage and versioned string-list outputs are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return GuardStringListOutput(error, list, view, [&](auto& result)
        {
            const SdfLayerHandleVector layers = stage->value->GetLayerStack(true);
            std::vector<std::string> identifiers;
            identifiers.reserve(layers.size());
            for (const SdfLayerHandle& layer : layers)
            {
                if (layer)
                {
                    identifiers.push_back(layer->GetIdentifier());
                }
            }

            result = std::make_unique<openusd_string_list>();
            FillStringList(result.get(), identifiers, view);
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_mute_layer(
    const openusd_stage* stage,
    const char* layer_identifier,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value ||
            layer_identifier == nullptr || layer_identifier[0] == '\0')
        {
            WriteError(error, "A valid stage and layer identifier are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const std::string identifier(layer_identifier);
            if (stage->value->IsLayerMuted(identifier))
            {
                return OPENUSD_STATUS_OK;
            }
            const SdfLayerHandle layer = FindLayerInStack(stage, identifier);
            if (!layer)
            {
                WriteError(error, "The requested layer identifier was not found in the stage layer stack.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (layer == stage->value->GetRootLayer() || layer == stage->value->GetSessionLayer())
            {
                WriteError(error, "The stage root and session layers cannot be muted.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }

            TfErrorMark mark;
            stage->value->MuteLayer(identifier);
            if (!mark.IsClean() || !stage->value->IsLayerMuted(identifier))
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not mute the requested layer." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_unmute_layer(
    const openusd_stage* stage,
    const char* layer_identifier,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value ||
            layer_identifier == nullptr || layer_identifier[0] == '\0')
        {
            WriteError(error, "A valid stage and layer identifier are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const std::string identifier(layer_identifier);
            if (!stage->value->IsLayerMuted(identifier))
            {
                if (FindLayerInStack(stage, identifier))
                {
                    return OPENUSD_STATUS_OK;
                }
                WriteError(error, "The requested layer identifier was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            TfErrorMark mark;
            stage->value->UnmuteLayer(identifier);
            if (!mark.IsClean() || stage->value->IsLayerMuted(identifier))
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not unmute the requested layer." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_is_layer_muted(
    const openusd_stage* stage,
    const char* layer_identifier,
    int32_t* muted,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(muted);
        if (stage == nullptr || !stage->value ||
            layer_identifier == nullptr || layer_identifier[0] == '\0' || muted == nullptr)
        {
            WriteError(error, "A valid stage, layer identifier, and result output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const std::string identifier(layer_identifier);
            if (!IsKnownLayerIdentifier(stage, identifier))
            {
                WriteError(error, "The requested layer identifier was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            *muted = stage->value->IsLayerMuted(identifier) ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_save(
    const openusd_stage* stage,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value)
        {
            WriteError(error, "A valid stage handle is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const bool saved = stage->value->GetRootLayer()->Save();
            if (!saved || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not save the root layer." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_reload(
    const openusd_stage* stage,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value)
        {
            WriteError(error, "A valid stage handle is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            stage->value->Reload();
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not reload the stage." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_export(
    const openusd_stage* stage,
    const char* path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || path == nullptr || path[0] == '\0')
        {
            WriteError(error, "A valid stage and export path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const bool exported = stage->value->Export(path);
            if (!exported || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not export the stage." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_start_time_code(
    const openusd_stage* stage,
    double* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (stage == nullptr || !stage->value || value == nullptr)
        {
            WriteError(error, "A valid stage and value output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            *value = stage->value->GetStartTimeCode();
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_set_start_time_code(
    const openusd_stage* stage,
    double value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !std::isfinite(value))
        {
            WriteError(error, "A valid stage and finite start time code are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            stage->value->SetStartTimeCode(value);
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the start time code." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_end_time_code(
    const openusd_stage* stage,
    double* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (stage == nullptr || !stage->value || value == nullptr)
        {
            WriteError(error, "A valid stage and value output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            *value = stage->value->GetEndTimeCode();
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_set_end_time_code(
    const openusd_stage* stage,
    double value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !std::isfinite(value))
        {
            WriteError(error, "A valid stage and finite end time code are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            stage->value->SetEndTimeCode(value);
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the end time code." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_frames_per_second(
    const openusd_stage* stage,
    double* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (stage == nullptr || !stage->value || value == nullptr)
        {
            WriteError(error, "A valid stage and value output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            *value = stage->value->GetFramesPerSecond();
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_set_frames_per_second(
    const openusd_stage* stage,
    double value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !std::isfinite(value) || value <= 0)
        {
            WriteError(error, "A valid stage and positive finite frames per second are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            stage->value->SetFramesPerSecond(value);
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set frames per second." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_time_codes_per_second(
    const openusd_stage* stage,
    double* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (stage == nullptr || !stage->value || value == nullptr)
        {
            WriteError(error, "A valid stage and value output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            *value = stage->value->GetTimeCodesPerSecond();
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_set_time_codes_per_second(
    const openusd_stage* stage,
    double value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !std::isfinite(value) || value <= 0)
        {
            WriteError(error, "A valid stage and positive finite time codes per second are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            stage->value->SetTimeCodesPerSecond(value);
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set time codes per second." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_world_bounds(
    const openusd_stage* stage,
    const char* target_prim_path,
    uint32_t purpose_mask,
    int32_t time_sampled,
    double time_code,
    openusd_bounds3d* bounds,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        uint32_t struct_size = 0;
        uint32_t requested_version = 0;
        if (bounds != nullptr)
        {
            std::memcpy(&struct_size, bounds, sizeof(struct_size));
            if (struct_size >= offsetof(openusd_bounds3d, version) + sizeof(uint32_t))
            {
                std::memcpy(
                    &requested_version,
                    reinterpret_cast<const unsigned char*>(bounds) +
                        offsetof(openusd_bounds3d, version),
                    sizeof(requested_version));
            }
        }

        // ABI_OUTPUT_INITIALIZATION
        ResetBounds3dOutput(bounds);
        Bounds3dFailureReset failure_reset(bounds);
        const bool stage_bounds =
            target_prim_path == nullptr || target_prim_path[0] == '\0';
        if (stage == nullptr || !stage->value || bounds == nullptr ||
            !IsAligned(bounds) || struct_size < sizeof(openusd_bounds3d) ||
            requested_version != OPENUSD_BOUNDS3D_VERSION ||
            (!stage_bounds && !IsValidPrimPath(target_prim_path)) ||
            (purpose_mask & ~OPENUSD_GEOM_PURPOSE_MASK_ALL) != 0 ||
            (time_sampled != 0 && time_sampled != 1) ||
            (time_sampled == 1 && !std::isfinite(time_code)))
        {
            WriteError(
                error,
                "A valid stage, optional absolute prim path, purpose mask, time, "
                "and aligned bounds output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        if (purpose_mask == 0)
        {
            bounds->is_valid = 1;
            failure_reset.Commit();
            return OPENUSD_STATUS_OK;
        }

        TfErrorMark mark;
        const UsdPrim prim = stage_bounds
            ? stage->value->GetPseudoRoot()
            : stage->value->GetPrimAtPath(SdfPath(target_prim_path));
        if (!prim || !prim.IsActive())
        {
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(
                    error,
                    message.empty()
                        ? "Could not resolve the requested world-bounds prim."
                        : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            bounds->is_valid = 1;
            failure_reset.Commit();
            return OPENUSD_STATUS_OK;
        }

        TfTokenVector purposes;
        purposes.reserve(4);
        if ((purpose_mask & OPENUSD_GEOM_PURPOSE_MASK_DEFAULT) != 0)
        {
            purposes.push_back(UsdGeomTokens->default_);
        }
        if ((purpose_mask & OPENUSD_GEOM_PURPOSE_MASK_PROXY) != 0)
        {
            purposes.push_back(UsdGeomTokens->proxy);
        }
        if ((purpose_mask & OPENUSD_GEOM_PURPOSE_MASK_RENDER) != 0)
        {
            purposes.push_back(UsdGeomTokens->render);
        }
        if ((purpose_mask & OPENUSD_GEOM_PURPOSE_MASK_GUIDE) != 0)
        {
            purposes.push_back(UsdGeomTokens->guide);
        }
        GfRange3d range;
        {
            UsdGeomBBoxCache cache(
                GetTimeCode(time_sampled, time_code),
                std::move(purposes),
                true);
            range = cache.ComputeWorldBound(prim).ComputeAlignedRange();
        }
        if (!mark.IsClean())
        {
            std::string message = ConsumeErrors(mark);
            WriteError(
                error,
                message.empty() ? "Could not compute the requested world bounds." : message);
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        if (range.IsEmpty())
        {
            bounds->is_valid = 1;
            failure_reset.Commit();
            return OPENUSD_STATUS_OK;
        }

        const GfVec3d minimum = range.GetMin();
        const GfVec3d maximum = range.GetMax();
        for (size_t index = 0; index < 3; ++index)
        {
            if (!std::isfinite(minimum[index]) || !std::isfinite(maximum[index]) ||
                minimum[index] > maximum[index] ||
                !std::isfinite(maximum[index] - minimum[index]))
            {
                WriteError(error, "The computed world bounds are not finite and ordered.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            bounds->minimum[index] = minimum[index];
            bounds->maximum[index] = maximum[index];
        }
        bounds->is_valid = 1;
        bounds->is_empty = 0;
        failure_reset.Commit();
        return OPENUSD_STATUS_OK;
    });
}

openusd_status openusd_stage_get_default_prim_path(
    const openusd_stage* stage,
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
        if (stage == nullptr || !stage->value)
        {
            WriteError(error, "A valid stage handle is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetDefaultPrim();
            if (!prim)
            {
                WriteError(error, "The stage has no valid default prim.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            return CopyString(prim.GetPath().GetString(), buffer, capacity, required);
        });

    });
}

openusd_status openusd_stage_set_default_prim(
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
                WriteError(error, "The default prim must exist on the stage.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            TfErrorMark mark;
            stage->value->SetDefaultPrim(prim);
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the default prim." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_clear_default_prim(
    const openusd_stage* stage,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value)
        {
            WriteError(error, "A valid stage handle is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            stage->value->ClearDefaultPrim();
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not clear the default prim." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_define_prim(
    const openusd_stage* stage,
    const char* prim_path,
    const char* type_name,
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
            const TfToken type = type_name == nullptr ? TfToken() : TfToken(type_name);
            const UsdPrim prim = stage->value->DefinePrim(SdfPath(prim_path), type);
            if (!prim || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not define the prim." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_override_prim(
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
            const UsdPrim prim = stage->value->OverridePrim(SdfPath(prim_path));
            if (!prim || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not override the prim." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_create_class_prim(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        const SdfPath path(prim_path == nullptr ? "" : prim_path);
        if (stage == nullptr || !stage->value || !path.IsAbsolutePath() || !path.IsRootPrimPath())
        {
            WriteError(error, "A valid stage and absolute root prim path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const UsdPrim prim = stage->value->CreateClassPrim(path);
            if (!prim || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not create the class prim." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_prim_paths(
    const openusd_stage* stage,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetStringListOutput(list, view);
        if (stage == nullptr || !stage->value || list == nullptr || view == nullptr ||
            view->struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(error, "A valid stage, list output, and versioned view are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return GuardStringListOutput(error, list, view, [&](auto& result)
        {
            std::vector<std::string> values;
            for (const UsdPrim& prim : stage->value->Traverse())
            {
                values.push_back(prim.GetPath().GetString());
            }

            result = std::make_unique<openusd_string_list>();
            FillStringList(result.get(), values, view);
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_prim_type_name(
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
                WriteError(error, "The requested prim was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            return CopyString(prim.GetTypeName().GetString(), buffer, capacity, required);
        });

    });
}

openusd_status openusd_stage_get_prim_applied_schemas(
    const openusd_stage* stage,
    const char* prim_path,
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
            list == nullptr || view == nullptr ||
            view->struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(error, "A valid stage, prim path, list output, and versioned view are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return GuardStringListOutput(error, list, view, [&](auto& result)
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, "The requested prim was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            std::vector<std::string> values;
            const TfTokenVector& schemas = prim.GetAppliedSchemas();
            values.reserve(schemas.size());
            for (const TfToken& schema : schemas)
            {
                values.push_back(schema.GetString());
            }

            result = std::make_unique<openusd_string_list>();
            FillStringList(result.get(), values, view);
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_prim_child_paths(
    const openusd_stage* stage,
    const char* prim_path,
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
            list == nullptr || view == nullptr ||
            view->struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(error, "A valid stage, prim path, list output, and versioned view are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return GuardStringListOutput(error, list, view, [&](auto& result)
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, "The requested prim was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            std::vector<std::string> values;
            for (const UsdPrim& child : prim.GetAllChildren())
            {
                values.push_back(child.GetPath().GetString());
            }

            result = std::make_unique<openusd_string_list>();
            FillStringList(result.get(), values, view);
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_prim_attribute_names(
    const openusd_stage* stage,
    const char* prim_path,
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
            list == nullptr || view == nullptr ||
            view->struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(error, "A valid stage, prim path, list output, and versioned view are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return GuardStringListOutput(error, list, view, [&](auto& result)
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, "The requested prim was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const std::vector<UsdAttribute> attributes = prim.GetAttributes();
            std::vector<std::string> values;
            values.reserve(attributes.size());
            for (const UsdAttribute& attribute : attributes)
            {
                values.push_back(attribute.GetName().GetString());
            }

            result = std::make_unique<openusd_string_list>();
            FillStringList(result.get(), values, view);
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_prim_relationship_names(
    const openusd_stage* stage,
    const char* prim_path,
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
            list == nullptr || view == nullptr ||
            view->struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(error, "A valid stage, prim path, list output, and versioned view are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return GuardStringListOutput(error, list, view, [&](auto& result)
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, "The requested prim was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const std::vector<UsdRelationship> relationships = prim.GetRelationships();
            std::vector<std::string> values;
            values.reserve(relationships.size());
            for (const UsdRelationship& relationship : relationships)
            {
                values.push_back(relationship.GetName().GetString());
            }

            result = std::make_unique<openusd_string_list>();
            FillStringList(result.get(), values, view);
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_attribute_type_name(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
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
            attribute_name == nullptr || attribute_name[0] == '\0')
        {
            WriteError(error, "A valid stage, prim path, and attribute name are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            const UsdAttribute attribute =
                prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
            if (!attribute)
            {
                WriteError(error, "The requested attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            return CopyString(attribute.GetTypeName().GetAsToken().GetString(), buffer, capacity, required);
        });

    });
}

openusd_status openusd_stage_get_attribute_value_state(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    int32_t* has_authored_value_opinion,
    int32_t* value_is_blocked,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(has_authored_value_opinion);
        ResetAbiOutput(value_is_blocked);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || attribute_name[0] == '\0' ||
            has_authored_value_opinion == nullptr || value_is_blocked == nullptr)
        {
            WriteError(error, "A valid stage, prim path, attribute name, and state outputs are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            const UsdAttribute attribute =
                prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
            if (!attribute)
            {
                WriteError(error, "The requested attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const UsdResolveInfo resolveInfo =
                attribute.GetResolveInfo(GetTimeCode(time_sampled, time_code));
            *has_authored_value_opinion = attribute.HasAuthoredValueOpinion() ? 1 : 0;
            *value_is_blocked = resolveInfo.ValueIsBlocked() ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_attribute_time_samples(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    double* values,
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
            if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
                attribute_name == nullptr || attribute_name[0] == '\0' || required == nullptr ||
                !IsValidArrayBuffer(values, capacity))
            {
                WriteError(
                    error,
                    "A valid stage, prim path, attribute name, aligned buffer, and size output are required.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }

            return Guard(error, [&]()
            {
                const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
                const UsdAttribute attribute =
                    prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
                if (!attribute)
                {
                    WriteError(error, "The requested attribute was not found.");
                    return OPENUSD_STATUS_NOT_FOUND;
                }

                TfErrorMark mark;
                std::vector<double> samples;
                const bool read = attribute.GetTimeSamples(&samples);
                if (!read || !mark.IsClean())
                {
                    const bool had_errors = !mark.IsClean();
                    std::string message = ConsumeErrors(mark);
                    WriteError(
                        error,
                        message.empty() ? "Could not read attribute time samples." : message);
                    return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
                }

                const size_t count = samples.size();
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
                std::copy(samples.begin(), samples.end(), values);
                *required = count;
                return OPENUSD_STATUS_OK;
            });
        });

    });
}

openusd_status openusd_stage_clear_attribute_value(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
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
            const UsdAttribute attribute =
                prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
            if (!attribute)
            {
                WriteError(error, "The requested attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            TfErrorMark mark;
            const bool cleared = attribute.Clear();
            if (!cleared || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not clear the attribute value." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_block_attribute_value(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
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
            const UsdAttribute attribute =
                prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
            if (!attribute)
            {
                WriteError(error, "The requested attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            TfErrorMark mark;
            attribute.Block();
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not block the attribute value." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_attribute_scalar_value(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    openusd_scalar_value* value,
    char* string_buffer,
    size_t string_capacity,
    size_t* string_required,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetVersionedAbiOutput(value);
        ResetAbiStringOutput(string_buffer, string_capacity);
        ResetAbiOutput(string_required);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || attribute_name[0] == '\0' ||
            value == nullptr ||
            value->struct_size < offsetof(openusd_scalar_value, matrix4d_value) ||
            string_required == nullptr)
        {
            WriteError(error, "A valid stage, attribute, versioned value, and string size output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        *string_required = 0;
        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            const UsdAttribute attribute =
                prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
            if (!attribute)
            {
                WriteError(error, "The requested attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const UsdTimeCode time = GetTimeCode(time_sampled, time_code);
            const SdfValueTypeName typeName = attribute.GetTypeName();
            TfErrorMark mark;
            bool read = false;

            if (typeName == SdfValueTypeNames->Bool)
            {
                bool result = false;
                read = attribute.Get(&result, time);
                value->kind = OPENUSD_SCALAR_KIND_BOOL;
                value->bool_value = result ? 1 : 0;
            }
            else if (typeName == SdfValueTypeNames->Int64)
            {
                read = attribute.Get(&value->int64_value, time);
                value->kind = OPENUSD_SCALAR_KIND_INT64;
            }
            else if (typeName == SdfValueTypeNames->Double)
            {
                read = attribute.Get(&value->double_value, time);
                value->kind = OPENUSD_SCALAR_KIND_DOUBLE;
            }
            else if (typeName == SdfValueTypeNames->String)
            {
                std::string result;
                read = attribute.Get(&result, time);
                value->kind = OPENUSD_SCALAR_KIND_STRING;
                if (read && mark.IsClean())
                {
                    return CopyString(result, string_buffer, string_capacity, string_required);
                }
            }
            else if (typeName == SdfValueTypeNames->Token)
            {
                TfToken result;
                read = attribute.Get(&result, time);
                value->kind = OPENUSD_SCALAR_KIND_TOKEN;
                if (read && mark.IsClean())
                {
                    return CopyString(result.GetString(), string_buffer, string_capacity, string_required);
                }
            }
            else if (typeName == SdfValueTypeNames->Float3 ||
                     typeName == SdfValueTypeNames->Vector3f ||
                     typeName == SdfValueTypeNames->Color3f)
            {
                GfVec3f result;
                read = attribute.Get(&result, time);
                value->kind = typeName == SdfValueTypeNames->Color3f
                    ? OPENUSD_SCALAR_KIND_COLOR3F
                    : OPENUSD_SCALAR_KIND_VEC3F;
                value->vec3f_value = {result[0], result[1], result[2]};
            }
            else if (typeName == SdfValueTypeNames->Matrix4d)
            {
                if (value->struct_size < sizeof(openusd_scalar_value))
                {
                    WriteError(error, "The tagged scalar value is too small for a matrix4d payload.");
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
                }
                GfMatrix4d result;
                read = attribute.Get(&result, time);
                value->kind = OPENUSD_SCALAR_KIND_MATRIX4D;
                for (int row = 0; row < 4; ++row)
                {
                    for (int column = 0; column < 4; ++column)
                    {
                        value->matrix4d_value.values[(row * 4) + column] = result[row][column];
                    }
                }
            }
            else
            {
                WriteError(
                    error,
                    std::string("The attribute type is not a supported scalar: ") +
                        typeName.GetAsToken().GetString());
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }

            if (!read || !mark.IsClean())
            {
                const bool hadErrors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                if (message.empty())
                {
                    message = attribute.GetResolveInfo(time).ValueIsBlocked()
                        ? "The attribute value is blocked."
                        : "The attribute has no readable scalar value.";
                }
                WriteError(error, message);
                return hadErrors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_set_double(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    double value,
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
            TfErrorMark mark;
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const TfToken name(attribute_name);
            UsdAttribute attribute = prim.GetAttribute(name);
            if (!attribute)
            {
                attribute = prim.CreateAttribute(name, SdfValueTypeNames->Double, true);
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->Double, "double", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }
            const bool set = attribute && attribute.Set(value, GetTimeCode(time_sampled, time_code));
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the double attribute." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_double(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    double* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || value == nullptr)
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
                WriteError(error, "The requested double attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->Double, "double", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }

            TfErrorMark mark;
            const bool read = attribute.Get(value, GetTimeCode(time_sampled, time_code));
            if (!read || !mark.IsClean())
            {
                const bool had_errors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not read the double attribute." : message);
                return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_set_double_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const double* values,
    size_t count,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || (values == nullptr && count != 0))
        {
            WriteError(error, "A valid stage, prim path, attribute name, and value buffer are required.");
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
            if (!attribute)
            {
                attribute = prim.CreateAttribute(name, SdfValueTypeNames->DoubleArray, true);
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->DoubleArray, "double array", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }

            VtArray<double> array(count);
            if (count != 0)
            {
                std::copy(values, values + count, array.begin());
            }
            const bool set = attribute && attribute.Set(array, GetTimeCode(time_sampled, time_code));
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the double array attribute." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_double_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    double* values,
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
            if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
                attribute_name == nullptr || attribute_name[0] == '\0' || required == nullptr ||
                !IsValidArrayBuffer(values, capacity))
            {
                WriteError(
                    error,
                    "A valid stage, prim path, attribute name, aligned buffer, and size output are required.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }

            return Guard(error, [&]()
            {
                const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
                const UsdAttribute attribute =
                    prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
                if (!attribute)
                {
                    WriteError(error, "The requested double array attribute was not found.");
                    return OPENUSD_STATUS_NOT_FOUND;
                }
                const openusd_status typeStatus = ValidateAttributeType(
                    attribute, SdfValueTypeNames->DoubleArray, "double array", error);
                if (typeStatus != OPENUSD_STATUS_OK)
                {
                    return typeStatus;
                }

                TfErrorMark mark;
                VtArray<double> array;
                const bool read = attribute.Get(&array, GetTimeCode(time_sampled, time_code));
                if (!read || !mark.IsClean())
                {
                    const bool had_errors = !mark.IsClean();
                    std::string message = ConsumeErrors(mark);
                    WriteError(
                        error,
                        message.empty() ? "Could not read the double array attribute." : message);
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
                std::copy(array.begin(), array.end(), values);
                *required = count;
                return OPENUSD_STATUS_OK;
            });
        });

    });
}

openusd_status openusd_stage_set_matrix4d(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const openusd_matrix4d* value,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || attribute_name[0] == '\0' ||
            value == nullptr || !IsAligned(value))
        {
            WriteError(error, "A valid stage, prim path, attribute name, and aligned matrix are required.");
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
            if (attribute && attribute.GetTypeName() != SdfValueTypeNames->Matrix4d)
            {
                WriteError(error, "The attribute is not a matrix4d.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            if (!attribute)
            {
                attribute = prim.CreateAttribute(name, SdfValueTypeNames->Matrix4d, true);
            }

            GfMatrix4d matrix(0.0);
            for (int row = 0; row < 4; ++row)
            {
                for (int column = 0; column < 4; ++column)
                {
                    matrix[row][column] = value->values[(row * 4) + column];
                }
            }
            const bool set = attribute && attribute.Set(matrix, GetTimeCode(time_sampled, time_code));
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the matrix4d attribute." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_matrix4d(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
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
            attribute_name == nullptr || attribute_name[0] == '\0' ||
            value == nullptr || !IsAligned(value))
        {
            WriteError(error, "A valid stage, prim path, attribute name, and aligned matrix output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            const UsdAttribute attribute =
                prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
            if (!attribute)
            {
                WriteError(error, "The requested matrix4d attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (attribute.GetTypeName() != SdfValueTypeNames->Matrix4d)
            {
                WriteError(error, "The attribute is not a matrix4d.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }

            TfErrorMark mark;
            const UsdTimeCode time = GetTimeCode(time_sampled, time_code);
            GfMatrix4d matrix;
            const bool read = attribute.Get(&matrix, time);
            if (!read || !mark.IsClean())
            {
                const bool had_errors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                if (message.empty())
                {
                    message = attribute.GetResolveInfo(time).ValueIsBlocked()
                        ? "The attribute value is blocked."
                        : "The attribute has no readable matrix4d value.";
                }
                WriteError(error, message);
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

openusd_status openusd_stage_set_int32_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const int32_t* values,
    size_t count,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        return SetArrayAttribute<int32_t, int>(
            stage,
            prim_path,
            attribute_name,
            values,
            count,
            time_sampled,
            time_code,
            SdfValueTypeNames->IntArray,
            "int32",
            [](const SdfValueTypeName& type) { return type == SdfValueTypeNames->IntArray; },
            [](int32_t value) { return static_cast<int>(value); },
            error);

    });
}

openusd_status openusd_stage_get_int32_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
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
            return GetArrayAttribute<int32_t, int>(
                stage,
                prim_path,
                attribute_name,
                time_sampled,
                time_code,
                values,
                capacity,
                required,
                "int32",
                [](const SdfValueTypeName& type) { return type == SdfValueTypeNames->IntArray; },
                [](int value) { return static_cast<int32_t>(value); },
                error);
        });

    });
}

openusd_status openusd_stage_set_float_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const float* values,
    size_t count,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        return SetArrayAttribute<float, float>(
            stage,
            prim_path,
            attribute_name,
            values,
            count,
            time_sampled,
            time_code,
            SdfValueTypeNames->FloatArray,
            "float",
            [](const SdfValueTypeName& type) { return type == SdfValueTypeNames->FloatArray; },
            [](float value) { return value; },
            error);

    });
}

openusd_status openusd_stage_get_float_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    float* values,
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
            return GetArrayAttribute<float, float>(
                stage,
                prim_path,
                attribute_name,
                time_sampled,
                time_code,
                values,
                capacity,
                required,
                "float",
                [](const SdfValueTypeName& type) { return type == SdfValueTypeNames->FloatArray; },
                [](float value) { return value; },
                error);
        });

    });
}

openusd_status openusd_stage_set_vec2f_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const openusd_vec2f* values,
    size_t count,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        return SetArrayAttribute<openusd_vec2f, GfVec2f>(
            stage,
            prim_path,
            attribute_name,
            values,
            count,
            time_sampled,
            time_code,
            SdfValueTypeNames->Float2Array,
            "vec2f",
            [](const SdfValueTypeName& type)
            {
                return type == SdfValueTypeNames->Float2Array ||
                    type == SdfValueTypeNames->TexCoord2fArray;
            },
            [](const openusd_vec2f& value) { return GfVec2f(value.x, value.y); },
            error);

    });
}

openusd_status openusd_stage_get_vec2f_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    openusd_vec2f* values,
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
            return GetArrayAttribute<openusd_vec2f, GfVec2f>(
                stage,
                prim_path,
                attribute_name,
                time_sampled,
                time_code,
                values,
                capacity,
                required,
                "vec2f",
                [](const SdfValueTypeName& type)
                {
                    return type == SdfValueTypeNames->Float2Array ||
                        type == SdfValueTypeNames->TexCoord2fArray;
                },
                [](const GfVec2f& value) { return openusd_vec2f{value[0], value[1]}; },
                error);
        });

    });
}

openusd_status openusd_stage_set_vec3f_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const openusd_vec3f* values,
    size_t count,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        return SetArrayAttribute<openusd_vec3f, GfVec3f>(
            stage,
            prim_path,
            attribute_name,
            values,
            count,
            time_sampled,
            time_code,
            SdfValueTypeNames->Float3Array,
            "vec3f",
            [](const SdfValueTypeName& type)
            {
                return type == SdfValueTypeNames->Float3Array;
            },
            [](const openusd_vec3f& value) { return GfVec3f(value.x, value.y, value.z); },
            error);

    });
}

openusd_status openusd_stage_get_vec3f_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
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
            return GetArrayAttribute<openusd_vec3f, GfVec3f>(
                stage,
                prim_path,
                attribute_name,
                time_sampled,
                time_code,
                values,
                capacity,
                required,
                "vec3f",
                [](const SdfValueTypeName& type)
                {
                    return type == SdfValueTypeNames->Float3Array;
                },
                [](const GfVec3f& value)
                {
                    return openusd_vec3f{value[0], value[1], value[2]};
                },
                error);
        });

    });
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

openusd_status openusd_stage_set_bool(
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
            TfErrorMark mark;
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const TfToken name(attribute_name);
            UsdAttribute attribute = prim.GetAttribute(name);
            if (!attribute)
            {
                attribute = prim.CreateAttribute(name, SdfValueTypeNames->Bool, true);
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->Bool, "bool", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }
            const bool set = attribute && attribute.Set(value != 0, GetTimeCode(time_sampled, time_code));
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the bool attribute." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_bool(
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
            attribute_name == nullptr || value == nullptr)
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
                WriteError(error, "The requested bool attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->Bool, "bool", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }

            TfErrorMark mark;
            bool result = false;
            const bool read = attribute.Get(&result, GetTimeCode(time_sampled, time_code));
            if (!read || !mark.IsClean())
            {
                const bool had_errors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not read the bool attribute." : message);
                return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            *value = result ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_set_int64(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int64_t value,
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
            TfErrorMark mark;
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const TfToken name(attribute_name);
            UsdAttribute attribute = prim.GetAttribute(name);
            if (!attribute)
            {
                attribute = prim.CreateAttribute(name, SdfValueTypeNames->Int64, true);
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->Int64, "int64", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }
            const bool set = attribute && attribute.Set(value, GetTimeCode(time_sampled, time_code));
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the int64 attribute." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_int64(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    int64_t* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || value == nullptr)
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
                WriteError(error, "The requested int64 attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->Int64, "int64", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }

            TfErrorMark mark;
            const bool read = attribute.Get(value, GetTimeCode(time_sampled, time_code));
            if (!read || !mark.IsClean())
            {
                const bool had_errors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not read the int64 attribute." : message);
                return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_set_string(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const char* value,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || attribute_name[0] == '\0' || value == nullptr)
        {
            WriteError(error, "A valid stage, prim path, attribute name, and value are required.");
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

            const TfToken name(attribute_name);
            UsdAttribute attribute = prim.GetAttribute(name);
            if (!attribute)
            {
                attribute = prim.CreateAttribute(name, SdfValueTypeNames->String, true);
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->String, "string", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }
            const bool set =
                attribute && attribute.Set(std::string(value), GetTimeCode(time_sampled, time_code));
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the string attribute." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_string(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
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
            attribute_name == nullptr || required == nullptr)
        {
            WriteError(error, "A valid stage, prim path, attribute name, and size output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            const UsdAttribute attribute = prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
            if (!attribute)
            {
                WriteError(error, "The requested string attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->String, "string", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }

            TfErrorMark mark;
            std::string result;
            const bool read = attribute.Get(&result, GetTimeCode(time_sampled, time_code));
            if (!read || !mark.IsClean())
            {
                const bool had_errors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not read the string attribute." : message);
                return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            return CopyString(result, buffer, capacity, required);
        });

    });
}

openusd_status openusd_stage_set_token(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const char* value,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || attribute_name[0] == '\0' || value == nullptr)
        {
            WriteError(error, "A valid stage, prim path, attribute name, and value are required.");
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

            const TfToken name(attribute_name);
            UsdAttribute attribute = prim.GetAttribute(name);
            if (!attribute)
            {
                attribute = prim.CreateAttribute(name, SdfValueTypeNames->Token, true);
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->Token, "token", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }
            const bool set =
                attribute && attribute.Set(TfToken(value), GetTimeCode(time_sampled, time_code));
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the token attribute." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_token(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
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
            attribute_name == nullptr || required == nullptr)
        {
            WriteError(error, "A valid stage, prim path, attribute name, and size output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            const UsdAttribute attribute = prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
            if (!attribute)
            {
                WriteError(error, "The requested token attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->Token, "token", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }

            TfErrorMark mark;
            TfToken result;
            const bool read = attribute.Get(&result, GetTimeCode(time_sampled, time_code));
            if (!read || !mark.IsClean())
            {
                const bool had_errors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not read the token attribute." : message);
                return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            return CopyString(result.GetString(), buffer, capacity, required);
        });

    });
}

openusd_status openusd_stage_set_vec3f(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const openusd_vec3f* value,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || attribute_name[0] == '\0' || value == nullptr)
        {
            WriteError(error, "A valid stage, prim path, attribute name, and value are required.");
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

            const TfToken name(attribute_name);
            UsdAttribute attribute = prim.GetAttribute(name);
            if (!attribute)
            {
                attribute = prim.CreateAttribute(name, SdfValueTypeNames->Float3, true);
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->Float3, "float3", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }
            const GfVec3f vector(value->x, value->y, value->z);
            const bool set = attribute && attribute.Set(vector, GetTimeCode(time_sampled, time_code));
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the vec3f attribute." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_vec3f(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    openusd_vec3f* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || value == nullptr)
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
                WriteError(error, "The requested vec3f attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->Float3, "float3", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }

            TfErrorMark mark;
            GfVec3f result;
            const bool read = attribute.Get(&result, GetTimeCode(time_sampled, time_code));
            if (!read || !mark.IsClean())
            {
                const bool had_errors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not read the vec3f attribute." : message);
                return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            value->x = result[0];
            value->y = result[1];
            value->z = result[2];
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_set_color3f(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const openusd_vec3f* value,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || attribute_name[0] == '\0' || value == nullptr)
        {
            WriteError(error, "A valid stage, prim path, attribute name, and value are required.");
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

            const TfToken name(attribute_name);
            UsdAttribute attribute = prim.GetAttribute(name);
            if (!attribute)
            {
                attribute = prim.CreateAttribute(name, SdfValueTypeNames->Color3f, true);
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->Color3f, "color3f", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }
            const GfVec3f vector(value->x, value->y, value->z);
            const bool set = attribute && attribute.Set(vector, GetTimeCode(time_sampled, time_code));
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the color3f attribute." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_color3f(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    openusd_vec3f* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || value == nullptr)
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
                WriteError(error, "The requested color3f attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->Color3f, "color3f", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }

            TfErrorMark mark;
            GfVec3f result;
            const bool read = attribute.Get(&result, GetTimeCode(time_sampled, time_code));
            if (!read || !mark.IsClean())
            {
                const bool had_errors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not read the color3f attribute." : message);
                return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            value->x = result[0];
            value->y = result[1];
            value->z = result[2];
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_has_prim(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* exists,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(exists);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) || exists == nullptr)
        {
            WriteError(error, "A valid stage, prim path, and output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            *exists = prim ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_remove_prim(
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
            const bool removed = stage->value->RemovePrim(SdfPath(prim_path));
            if (!removed || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "The prim was not found." : message);
                return removed ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_set_prim_active(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t active,
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

            const bool set = prim.SetActive(active != 0);
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the prim active state." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_prim_active(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* active,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(active);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) || active == nullptr)
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
            *active = prim.IsActive() ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });

    });
}

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

openusd_status openusd_stage_set_prim_metadata(
    const openusd_stage* stage,
    const char* prim_path,
    const char* key,
    const openusd_metadata_value* value,
    const char* string_value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            key == nullptr || key[0] == '\0' || value == nullptr ||
            value->struct_size < sizeof(openusd_metadata_value))
        {
            WriteError(error, "A valid stage, prim path, key, and versioned value are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const VtValue vtValue = MakeMetadataValue(value, string_value);
            TfErrorMark mark;
            prim.SetCustomDataByKey(TfToken(key), vtValue);
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the prim metadata." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_prim_metadata(
    const openusd_stage* stage,
    const char* prim_path,
    const char* key,
    int32_t requested_kind,
    openusd_metadata_value* value,
    char* string_buffer,
    size_t string_capacity,
    size_t* string_required,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetVersionedAbiOutput(value);
        ResetAbiStringOutput(string_buffer, string_capacity);
        ResetAbiOutput(string_required);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            key == nullptr || key[0] == '\0' || value == nullptr ||
            value->struct_size < sizeof(openusd_metadata_value))
        {
            WriteError(error, "A valid stage, prim path, key, and versioned value output are required.");
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

            const VtValue stored = prim.GetCustomDataByKey(TfToken(key));
            if (stored.IsEmpty())
            {
                WriteError(error, "The requested prim metadata was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            return ReadMetadataValue(
                stored, requested_kind, value, string_buffer, string_capacity, string_required, error);
        });

    });
}

openusd_status openusd_stage_clear_prim_metadata(
    const openusd_stage* stage,
    const char* prim_path,
    const char* key,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            key == nullptr || key[0] == '\0')
        {
            WriteError(error, "A valid stage, prim path, and key are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const TfToken keyToken(key);
            if (!prim.HasCustomDataKey(keyToken))
            {
                WriteError(error, "The requested prim metadata was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            TfErrorMark mark;
            prim.ClearCustomDataByKey(keyToken);
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not clear the prim metadata." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_change_serial(
    const openusd_stage* stage,
    uint64_t* serial,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(serial);
        if (stage == nullptr || !stage->value || serial == nullptr)
        {
            WriteError(error, "A valid stage and serial output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        *serial = stage->change_serial.load(std::memory_order_relaxed);
        return OPENUSD_STATUS_OK;

    });
}

void openusd_layer_release(openusd_layer* layer)
{
    ReleaseLayer(layer);
}

openusd_status openusd_layer_get_identifier(
    const openusd_layer* layer,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiStringOutput(buffer, capacity);
        ResetAbiOutput(required);
        if (layer == nullptr || !layer->value)
        {
            WriteError(error, "A valid layer handle is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            return CopyString(layer->value->GetIdentifier(), buffer, capacity, required);
        });

    });
}

openusd_status openusd_layer_save(
    const openusd_layer* layer,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]() -> openusd_status
    {
        if (layer == nullptr || !layer->value)
        {
            WriteError(error, "A valid layer handle is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const bool saved = layer->value->Save();
            if (!saved || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not save the layer." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_layer_reload(
    const openusd_layer* layer,
    int32_t force,
    int32_t* reloaded,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(reloaded);
        if (layer == nullptr || !layer->value || reloaded == nullptr)
        {
            WriteError(error, "A valid layer handle and reload output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const bool didReload = layer->value->Reload(force != 0);
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not reload the layer." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            *reloaded = didReload ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_layer_export(
    const openusd_layer* layer,
    const char* path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]() -> openusd_status
    {
        if (layer == nullptr || !layer->value || path == nullptr || path[0] == '\0')
        {
            WriteError(error, "A valid layer and export path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const bool exported = layer->value->Export(path);
            if (!exported || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not export the layer." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_layer_add_sublayer(
    const openusd_layer* layer,
    const char* sublayer_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]() -> openusd_status
    {
        if (layer == nullptr || !layer->value || sublayer_path == nullptr || sublayer_path[0] == '\0')
        {
            WriteError(error, "A valid layer handle and sublayer path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            layer->value->InsertSubLayerPath(sublayer_path);
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not add the sublayer." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_layer_remove_sublayer(
    const openusd_layer* layer,
    const char* sublayer_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]() -> openusd_status
    {
        if (layer == nullptr || !layer->value || sublayer_path == nullptr || sublayer_path[0] == '\0')
        {
            WriteError(error, "A valid layer handle and sublayer path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const SdfSubLayerProxy paths = layer->value->GetSubLayerPaths();
            int index = -1;
            for (size_t i = 0; i < paths.size(); ++i)
            {
                if (paths[i] == sublayer_path)
                {
                    index = static_cast<int>(i);
                    break;
                }
            }
            if (index < 0)
            {
                WriteError(error, "The requested sublayer was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            TfErrorMark mark;
            layer->value->RemoveSubLayerPath(index);
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not remove the sublayer." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_layer_get_sublayer_paths(
    const openusd_layer* layer,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetStringListOutput(list, view);
        if (layer == nullptr || !layer->value || list == nullptr || view == nullptr ||
            view->struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(error, "A valid layer, list output, and versioned view are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return GuardStringListOutput(error, list, view, [&](auto& result)
        {
            const SdfSubLayerProxy paths = layer->value->GetSubLayerPaths();
            const std::vector<std::string> values(paths.begin(), paths.end());
            result = std::make_unique<openusd_string_list>();
            FillStringList(result.get(), values, view);
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_layer_set_metadata(
    const openusd_layer* layer,
    const char* key,
    const openusd_metadata_value* value,
    const char* string_value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]() -> openusd_status
    {
        if (layer == nullptr || !layer->value || key == nullptr || key[0] == '\0' ||
            value == nullptr || value->struct_size < sizeof(openusd_metadata_value))
        {
            WriteError(error, "A valid layer, key, and versioned value are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            VtDictionary data = layer->value->GetCustomLayerData();
            data[std::string(key)] = MakeMetadataValue(value, string_value);
            layer->value->SetCustomLayerData(data);
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the layer metadata." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_layer_get_metadata(
    const openusd_layer* layer,
    const char* key,
    int32_t requested_kind,
    openusd_metadata_value* value,
    char* string_buffer,
    size_t string_capacity,
    size_t* string_required,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetVersionedAbiOutput(value);
        ResetAbiStringOutput(string_buffer, string_capacity);
        ResetAbiOutput(string_required);
        if (layer == nullptr || !layer->value || key == nullptr || key[0] == '\0' ||
            value == nullptr || value->struct_size < sizeof(openusd_metadata_value))
        {
            WriteError(error, "A valid layer, key, and versioned value output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const VtDictionary data = layer->value->GetCustomLayerData();
            const auto entry = data.find(std::string(key));
            if (entry == data.end())
            {
                WriteError(error, "The requested layer metadata was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            return ReadMetadataValue(
                entry->second,
                requested_kind,
                value,
                string_buffer,
                string_capacity,
                string_required,
                error);
        });

    });
}

openusd_status openusd_layer_clear_metadata(
    const openusd_layer* layer,
    const char* key,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]() -> openusd_status
    {
        if (layer == nullptr || !layer->value || key == nullptr || key[0] == '\0')
        {
            WriteError(error, "A valid layer and key are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            VtDictionary data = layer->value->GetCustomLayerData();
            const auto entry = data.find(std::string(key));
            if (entry == data.end())
            {
                WriteError(error, "The requested layer metadata was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            TfErrorMark mark;
            data.erase(entry);
            layer->value->SetCustomLayerData(data);
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not clear the layer metadata." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_shade_is_material(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* is_material,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(is_material);
        if (stage == nullptr || !stage->value || is_material == nullptr ||
            !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage, absolute prim path, and result output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
        *is_material = prim && prim.IsA<UsdShadeMaterial>() ? 1 : 0;
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_shade_is_shader(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* is_shader,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(is_shader);
        if (stage == nullptr || !stage->value || is_shader == nullptr ||
            !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage, absolute prim path, and result output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
        *is_shader = prim && prim.IsA<UsdShadeShader>() ? 1 : 0;
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_shade_define_material(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage and absolute material prim path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            const UsdShadeMaterial material =
                UsdShadeMaterial::Define(stage->value, SdfPath(prim_path));
            if (!material)
            {
                WriteError(error, "Could not define the UsdShadeMaterial prim.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_shade_define_shader(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage and absolute shader prim path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            const UsdShadeShader shader =
                UsdShadeShader::Define(stage->value, SdfPath(prim_path));
            if (!shader)
            {
                WriteError(error, "Could not define the UsdShadeShader prim.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_shade_shader_set_source_id(
    const openusd_stage* stage,
    const char* shader_path,
    const char* source_id,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || source_id == nullptr ||
            source_id[0] == '\0' || !IsValidPrimPath(shader_path))
        {
            WriteError(error, "A valid stage, shader path, and source identifier are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdShadeShader shader(stage->value->GetPrimAtPath(SdfPath(shader_path)));
        if (!shader)
        {
            WriteError(error, "The requested prim is not a UsdShadeShader.");
            return OPENUSD_STATUS_NOT_FOUND;
        }
        if (!shader.SetShaderId(TfToken(source_id)))
        {
            WriteError(error, "Could not author the shader source identifier.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_shade_shader_get_source_id(
    const openusd_stage* stage,
    const char* shader_path,
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
        if (stage == nullptr || !stage->value || required == nullptr ||
            !IsValidPrimPath(shader_path))
        {
            WriteError(error, "A valid stage, shader path, and size output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdShadeShader shader(stage->value->GetPrimAtPath(SdfPath(shader_path)));
        if (!shader)
        {
            WriteError(error, "The requested prim is not a UsdShadeShader.");
            return OPENUSD_STATUS_NOT_FOUND;
        }
        TfToken sourceId;
        if (!shader.GetShaderId(&sourceId))
        {
            WriteError(error, "The shader has no source identifier.");
            return OPENUSD_STATUS_NOT_FOUND;
        }
        return CopyString(sourceId.GetString(), buffer, capacity, required);

    });
}

openusd_status openusd_shade_create_input(
    const openusd_stage* stage,
    const char* connectable_path,
    const char* input_name,
    openusd_shade_value_type value_type,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || input_name == nullptr ||
            input_name[0] == '\0' || !IsValidPrimPath(connectable_path))
        {
            WriteError(error, "A valid stage, connectable path, and input name are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const SdfValueTypeName type = GetShadeValueType(value_type);
        if (!type)
        {
            WriteError(error, "The shader input value type is unsupported.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdShadeConnectableAPI connectable =
            GetRequiredConnectable(stage, connectable_path, error);
        if (!connectable)
        {
            return OPENUSD_STATUS_NOT_FOUND;
        }
        const UsdShadeInput existing = connectable.GetInput(TfToken(input_name));
        if (existing && existing.GetTypeName() != type)
        {
            WriteError(error, "An input with the requested name exists with a different type.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (!connectable.CreateInput(TfToken(input_name), type))
        {
            WriteError(error, "Could not create the shader input.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_shade_get_input_type(
    const openusd_stage* stage,
    const char* connectable_path,
    const char* input_name,
    openusd_shade_value_type* value_type,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value_type);
        if (stage == nullptr || !stage->value || value_type == nullptr)
        {
            WriteError(error, "A valid stage and value-type output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdShadeInput input =
            GetRequiredShadeInput(stage, connectable_path, input_name, error);
        if (!input)
        {
            return IsValidPrimPath(connectable_path)
                ? OPENUSD_STATUS_NOT_FOUND
                : OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        *value_type = GetShadeValueType(input.GetTypeName());
        if (*value_type == OPENUSD_SHADE_VALUE_INVALID)
        {
            WriteError(error, "The shader input uses an unsupported value type.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_shade_set_input_float(
    const openusd_stage* stage,
    const char* shader_path,
    const char* input_name,
    float value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value)
        {
            WriteError(error, "A valid stage is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return SetShadeInputValue(
            stage, shader_path, input_name, OPENUSD_SHADE_VALUE_FLOAT, value, error);

    });
}

openusd_status openusd_shade_get_input_float(
    const openusd_stage* stage,
    const char* shader_path,
    const char* input_name,
    float* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (stage == nullptr || !stage->value || value == nullptr)
        {
            WriteError(error, "A valid stage and float output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return GetShadeInputValue(
            stage, shader_path, input_name, OPENUSD_SHADE_VALUE_FLOAT, value, error);

    });
}

openusd_status openusd_shade_set_input_vec3f(
    const openusd_stage* stage,
    const char* shader_path,
    const char* input_name,
    openusd_shade_value_type value_type,
    openusd_vec3f value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value ||
            (value_type != OPENUSD_SHADE_VALUE_COLOR3F &&
             value_type != OPENUSD_SHADE_VALUE_VECTOR3F &&
             value_type != OPENUSD_SHADE_VALUE_NORMAL3F))
        {
            WriteError(error, "A valid stage and vec3-compatible shader value type are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return SetShadeInputValue(
            stage,
            shader_path,
            input_name,
            value_type,
            GfVec3f(value.x, value.y, value.z),
            error);

    });
}

openusd_status openusd_shade_get_input_vec3f(
    const openusd_stage* stage,
    const char* shader_path,
    const char* input_name,
    openusd_shade_value_type value_type,
    openusd_vec3f* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (stage == nullptr || !stage->value || value == nullptr ||
            (value_type != OPENUSD_SHADE_VALUE_COLOR3F &&
             value_type != OPENUSD_SHADE_VALUE_VECTOR3F &&
             value_type != OPENUSD_SHADE_VALUE_NORMAL3F))
        {
            WriteError(error, "A valid stage, vec3-compatible value type, and output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        GfVec3f nativeValue;
        const openusd_status status = GetShadeInputValue(
            stage, shader_path, input_name, value_type, &nativeValue, error);
        if (status == OPENUSD_STATUS_OK)
        {
            value->x = nativeValue[0];
            value->y = nativeValue[1];
            value->z = nativeValue[2];
        }
        return status;

    });
}

openusd_status openusd_shade_set_input_string(
    const openusd_stage* stage,
    const char* shader_path,
    const char* input_name,
    openusd_shade_value_type value_type,
    const char* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || value == nullptr ||
            (value_type != OPENUSD_SHADE_VALUE_TOKEN &&
             value_type != OPENUSD_SHADE_VALUE_STRING &&
             value_type != OPENUSD_SHADE_VALUE_ASSET))
        {
            WriteError(error, "A valid stage, string-like value type, and value are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (value_type == OPENUSD_SHADE_VALUE_TOKEN)
        {
            return SetShadeInputValue(
                stage, shader_path, input_name, value_type, TfToken(value), error);
        }
        if (value_type == OPENUSD_SHADE_VALUE_ASSET)
        {
            return SetShadeInputValue(
                stage, shader_path, input_name, value_type, SdfAssetPath(value), error);
        }
        return SetShadeInputValue(
            stage, shader_path, input_name, value_type, std::string(value), error);

    });
}

openusd_status openusd_shade_get_input_string(
    const openusd_stage* stage,
    const char* shader_path,
    const char* input_name,
    openusd_shade_value_type value_type,
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
        if (stage == nullptr || !stage->value || required == nullptr ||
            (value_type != OPENUSD_SHADE_VALUE_TOKEN &&
             value_type != OPENUSD_SHADE_VALUE_STRING &&
             value_type != OPENUSD_SHADE_VALUE_ASSET))
        {
            WriteError(error, "A valid stage, string-like value type, and size output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (value_type == OPENUSD_SHADE_VALUE_TOKEN)
        {
            TfToken value;
            const openusd_status status = GetShadeInputValue(
                stage, shader_path, input_name, value_type, &value, error);
            return status == OPENUSD_STATUS_OK
                ? CopyString(value.GetString(), buffer, capacity, required)
                : status;
        }
        if (value_type == OPENUSD_SHADE_VALUE_ASSET)
        {
            SdfAssetPath value;
            const openusd_status status = GetShadeInputValue(
                stage, shader_path, input_name, value_type, &value, error);
            return status == OPENUSD_STATUS_OK
                ? CopyString(value.GetAssetPath(), buffer, capacity, required)
                : status;
        }
        std::string value;
        const openusd_status status = GetShadeInputValue(
            stage, shader_path, input_name, value_type, &value, error);
        return status == OPENUSD_STATUS_OK
            ? CopyString(value, buffer, capacity, required)
            : status;

    });
}

openusd_status openusd_shade_create_output(
    const openusd_stage* stage,
    const char* connectable_path,
    const char* output_name,
    openusd_shade_value_type value_type,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || output_name == nullptr ||
            output_name[0] == '\0' || !IsValidPrimPath(connectable_path))
        {
            WriteError(error, "A valid stage, connectable path, and output name are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const SdfValueTypeName type = GetShadeValueType(value_type);
        if (!type)
        {
            WriteError(error, "The shader output value type is unsupported.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdShadeConnectableAPI connectable =
            GetRequiredConnectable(stage, connectable_path, error);
        if (!connectable)
        {
            return OPENUSD_STATUS_NOT_FOUND;
        }
        const UsdShadeOutput existing = connectable.GetOutput(TfToken(output_name));
        if (existing && existing.GetTypeName() != type)
        {
            WriteError(error, "An output with the requested name exists with a different type.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (!connectable.CreateOutput(TfToken(output_name), type))
        {
            WriteError(error, "Could not create the shader output.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_shade_get_output_type(
    const openusd_stage* stage,
    const char* connectable_path,
    const char* output_name,
    openusd_shade_value_type* value_type,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value_type);
        if (stage == nullptr || !stage->value || value_type == nullptr)
        {
            WriteError(error, "A valid stage and value-type output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdShadeOutput output =
            GetRequiredShadeOutput(stage, connectable_path, output_name, error);
        if (!output)
        {
            return IsValidPrimPath(connectable_path)
                ? OPENUSD_STATUS_NOT_FOUND
                : OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        *value_type = GetShadeValueType(output.GetTypeName());
        if (*value_type == OPENUSD_SHADE_VALUE_INVALID)
        {
            WriteError(error, "The shader output uses an unsupported value type.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_shade_connect(
    const openusd_stage* stage,
    const char* destination_path,
    const char* destination_name,
    openusd_shade_attribute_type destination_type,
    const char* source_path,
    const char* source_name,
    openusd_shade_attribute_type source_type,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value ||
            GetShadeAttributeType(destination_type) == UsdShadeAttributeType::Invalid ||
            GetShadeAttributeType(source_type) == UsdShadeAttributeType::Invalid)
        {
            WriteError(error, "A valid stage and input/output attribute kinds are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        const UsdShadeConnectableAPI source =
            GetRequiredConnectable(stage, source_path, error);
        if (!source)
        {
            return IsValidPrimPath(source_path)
                ? OPENUSD_STATUS_NOT_FOUND
                : OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const TfToken sourceName(source_name == nullptr ? "" : source_name);
        const UsdShadeInput sourceInput =
            source_type == OPENUSD_SHADE_ATTRIBUTE_INPUT
                ? source.GetInput(sourceName)
                : UsdShadeInput();
        const UsdShadeOutput sourceOutput =
            source_type == OPENUSD_SHADE_ATTRIBUTE_OUTPUT
                ? source.GetOutput(sourceName)
                : UsdShadeOutput();
        const SdfValueTypeName sourceValueType =
            sourceInput ? sourceInput.GetTypeName()
                        : sourceOutput ? sourceOutput.GetTypeName() : SdfValueTypeName();
        if (!sourceValueType)
        {
            WriteError(error, "The requested shading source attribute does not exist.");
            return OPENUSD_STATUS_NOT_FOUND;
        }

        if (destination_type == OPENUSD_SHADE_ATTRIBUTE_INPUT)
        {
            const UsdShadeInput destination =
                GetRequiredShadeInput(stage, destination_path, destination_name, error);
            if (!destination)
            {
                return IsValidPrimPath(destination_path)
                    ? OPENUSD_STATUS_NOT_FOUND
                    : OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            if (!AreShadeConnectionTypesCompatible(
                    sourceValueType, destination.GetTypeName()))
            {
                WriteError(error, "The source and destination shading types do not match.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            if (!destination.ConnectToSource(
                    source, sourceName, GetShadeAttributeType(source_type)))
            {
                WriteError(error, "Could not connect the shader input to the requested source.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        }

        const UsdShadeOutput destination =
            GetRequiredShadeOutput(stage, destination_path, destination_name, error);
        if (!destination)
        {
            return IsValidPrimPath(destination_path)
                ? OPENUSD_STATUS_NOT_FOUND
                : OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (!AreShadeConnectionTypesCompatible(
                sourceValueType, destination.GetTypeName()))
        {
            WriteError(error, "The source and destination shading types do not match.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (!destination.ConnectToSource(
                source, sourceName, GetShadeAttributeType(source_type)))
        {
            WriteError(error, "Could not connect the shader output to the requested source.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_shade_disconnect(
    const openusd_stage* stage,
    const char* destination_path,
    const char* destination_name,
    openusd_shade_attribute_type destination_type,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value)
        {
            WriteError(error, "A valid stage is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        bool disconnected = false;
        if (destination_type == OPENUSD_SHADE_ATTRIBUTE_INPUT)
        {
            const UsdShadeInput input =
                GetRequiredShadeInput(stage, destination_path, destination_name, error);
            if (!input)
            {
                return IsValidPrimPath(destination_path)
                    ? OPENUSD_STATUS_NOT_FOUND
                    : OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            disconnected = input.DisconnectSource();
        }
        else if (destination_type == OPENUSD_SHADE_ATTRIBUTE_OUTPUT)
        {
            const UsdShadeOutput output =
                GetRequiredShadeOutput(stage, destination_path, destination_name, error);
            if (!output)
            {
                return IsValidPrimPath(destination_path)
                    ? OPENUSD_STATUS_NOT_FOUND
                    : OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            disconnected = output.DisconnectSource();
        }
        else
        {
            WriteError(error, "The destination must be a shading input or output.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (!disconnected)
        {
            WriteError(error, "Could not disconnect the shading property.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_shade_get_connected_source(
    const openusd_stage* stage,
    const char* destination_path,
    const char* destination_name,
    openusd_shade_attribute_type destination_type,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_shade_attribute_type* source_type,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetStringListOutput(list, view);
        ResetAbiOutput(source_type);
        if (stage == nullptr || !stage->value || list == nullptr || view == nullptr ||
            source_type == nullptr || view->struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(error, "A valid stage and versioned connected-source outputs are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        const openusd_status status =
            GuardStringListOutput(error, list, view, [&](auto& result)
        {
            if (destination_type == OPENUSD_SHADE_ATTRIBUTE_INPUT)
            {
                const UsdShadeInput input =
                    GetRequiredShadeInput(stage, destination_path, destination_name, error);
                if (!input)
                {
                    return IsValidPrimPath(destination_path)
                        ? OPENUSD_STATUS_NOT_FOUND
                        : OPENUSD_STATUS_INVALID_ARGUMENT;
                }
                return GetConnectedShadeSource(input, result, view, source_type, error);
            }
            if (destination_type == OPENUSD_SHADE_ATTRIBUTE_OUTPUT)
            {
                const UsdShadeOutput output =
                    GetRequiredShadeOutput(stage, destination_path, destination_name, error);
                if (!output)
                {
                    return IsValidPrimPath(destination_path)
                        ? OPENUSD_STATUS_NOT_FOUND
                        : OPENUSD_STATUS_INVALID_ARGUMENT;
                }
                return GetConnectedShadeSource(output, result, view, source_type, error);
            }
            WriteError(error, "The destination must be a shading input or output.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        });
        if (status != OPENUSD_STATUS_OK)
        {
            ResetAbiOutput(source_type);
        }
        return status;

    });
}

openusd_status openusd_shade_get_connected_sources(
    const openusd_stage* stage,
    const char* destination_path,
    const char* destination_name,
    openusd_shade_attribute_type destination_type,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetStringListOutput(list, view);
        if (stage == nullptr || !stage->value || list == nullptr || view == nullptr ||
            view->struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(error, "A valid stage and ABI v2 connected-source outputs are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return GuardStringListOutput(error, list, view, [&](auto& result)
        {
            if (destination_type == OPENUSD_SHADE_ATTRIBUTE_INPUT)
            {
                const UsdShadeInput input =
                    GetRequiredShadeInput(stage, destination_path, destination_name, error);
                return input
                    ? GetConnectedShadeSources(input, result, view, error)
                    : (IsValidPrimPath(destination_path)
                        ? OPENUSD_STATUS_NOT_FOUND
                        : OPENUSD_STATUS_INVALID_ARGUMENT);
            }
            if (destination_type == OPENUSD_SHADE_ATTRIBUTE_OUTPUT)
            {
                const UsdShadeOutput output =
                    GetRequiredShadeOutput(stage, destination_path, destination_name, error);
                return output
                    ? GetConnectedShadeSources(output, result, view, error)
                    : (IsValidPrimPath(destination_path)
                        ? OPENUSD_STATUS_NOT_FOUND
                        : OPENUSD_STATUS_INVALID_ARGUMENT);
            }
            WriteError(error, "The destination must be a shading input or output.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        });
    });
}

openusd_status openusd_shade_material_create_surface_output(
    const openusd_stage* stage,
    const char* material_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(material_path))
        {
            WriteError(error, "A valid stage and material path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdShadeMaterial material(stage->value->GetPrimAtPath(SdfPath(material_path)));
        if (!material)
        {
            WriteError(error, "The requested prim is not a UsdShadeMaterial.");
            return OPENUSD_STATUS_NOT_FOUND;
        }
        if (!material.CreateSurfaceOutput())
        {
            WriteError(error, "Could not create the material surface output.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_shade_material_bind(
    const openusd_stage* stage,
    const char* prim_path,
    const char* material_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            !IsValidPrimPath(material_path))
        {
            WriteError(error, "A valid stage, prim path, and material path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdPrim prim = GetRequiredPrim(stage, prim_path, error);
        if (!prim)
        {
            return OPENUSD_STATUS_NOT_FOUND;
        }
        const UsdShadeMaterial material(stage->value->GetPrimAtPath(SdfPath(material_path)));
        if (!material)
        {
            WriteError(error, "The requested material prim is missing or has the wrong schema.");
            return OPENUSD_STATUS_NOT_FOUND;
        }
        std::string whyNot;
        if (!UsdShadeMaterialBindingAPI::CanApply(prim, &whyNot))
        {
            WriteError(
                error,
                whyNot.empty() ? "MaterialBindingAPI cannot be applied to the prim." : whyNot);
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdShadeMaterialBindingAPI binding =
            UsdShadeMaterialBindingAPI::Apply(prim);
        if (!binding || !binding.Bind(material))
        {
            WriteError(error, "Could not bind the material to the prim.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_shade_material_unbind(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage and prim path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdPrim prim = GetRequiredPrim(stage, prim_path, error);
        if (!prim)
        {
            return OPENUSD_STATUS_NOT_FOUND;
        }
        const UsdShadeMaterialBindingAPI binding(prim);
        if (!binding || !binding.UnbindDirectBinding())
        {
            WriteError(error, "Could not remove the direct material binding.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_shade_get_direct_material(
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
        if (stage == nullptr || !stage->value || required == nullptr ||
            !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage, prim path, and size output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdPrim prim = GetRequiredPrim(stage, prim_path, error);
        if (!prim)
        {
            return OPENUSD_STATUS_NOT_FOUND;
        }
        const UsdShadeMaterial material =
            UsdShadeMaterialBindingAPI(prim).GetDirectBinding().GetMaterial();
        if (!material)
        {
            WriteError(error, "The prim has no directly bound material.");
            return OPENUSD_STATUS_NOT_FOUND;
        }
        return CopyString(
            material.GetPrim().GetPath().GetString(), buffer, capacity, required);

    });
}

openusd_status openusd_lux_is_schema(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_schema_kind schema_kind,
    int32_t* is_schema,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(is_schema);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            is_schema == nullptr || schema_kind < OPENUSD_LUX_SCHEMA_DISTANT_LIGHT ||
            schema_kind > OPENUSD_LUX_SCHEMA_CYLINDER_LIGHT)
        {
            WriteError(error, "A valid stage, absolute light path, schema kind, and result are required.");
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
            *is_schema = IsLuxSchema(prim, schema_kind) ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_lux_define(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_schema_kind schema_kind,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            schema_kind < OPENUSD_LUX_SCHEMA_DISTANT_LIGHT ||
            schema_kind > OPENUSD_LUX_SCHEMA_CYLINDER_LIGHT)
        {
            WriteError(error, "A valid stage, absolute light path, and schema kind are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            const SdfPath path(prim_path);
            UsdPrim prim;
            switch (schema_kind)
            {
                case OPENUSD_LUX_SCHEMA_DISTANT_LIGHT:
                    prim = UsdLuxDistantLight::Define(stage->value, path).GetPrim();
                    break;
                case OPENUSD_LUX_SCHEMA_SPHERE_LIGHT:
                    prim = UsdLuxSphereLight::Define(stage->value, path).GetPrim();
                    break;
                case OPENUSD_LUX_SCHEMA_RECT_LIGHT:
                    prim = UsdLuxRectLight::Define(stage->value, path).GetPrim();
                    break;
                case OPENUSD_LUX_SCHEMA_DISK_LIGHT:
                    prim = UsdLuxDiskLight::Define(stage->value, path).GetPrim();
                    break;
                case OPENUSD_LUX_SCHEMA_DOME_LIGHT:
                    prim = UsdLuxDomeLight::Define(stage->value, path).GetPrim();
                    break;
                case OPENUSD_LUX_SCHEMA_CYLINDER_LIGHT:
                    prim = UsdLuxCylinderLight::Define(stage->value, path).GetPrim();
                    break;
                default:
                    break;
            }
            if (!prim || !IsLuxSchema(prim, schema_kind))
            {
                WriteError(error, "Could not define the requested UsdLux light schema.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_lux_set_float(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_float_property property,
    float value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!std::isfinite(value))
        {
            WriteError(error, "A finite light value is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (property == OPENUSD_LUX_FLOAT_INTENSITY && value < 0.0F)
        {
            WriteError(error, "Light intensity must be non-negative.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (property == OPENUSD_LUX_FLOAT_COLOR_TEMPERATURE &&
            (value < 1000.0F || value > 10000.0F))
        {
            WriteError(error, "Light color temperature must be between 1000K and 10000K.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &light, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        switch (property)
        {
            case OPENUSD_LUX_FLOAT_INTENSITY:
                return SetLuxAttribute(light.CreateIntensityAttr(), value, "light intensity", error);
            case OPENUSD_LUX_FLOAT_EXPOSURE:
                return SetLuxAttribute(light.CreateExposureAttr(), value, "light exposure", error);
            case OPENUSD_LUX_FLOAT_DIFFUSE:
                return SetLuxAttribute(light.CreateDiffuseAttr(), value, "light diffuse multiplier", error);
            case OPENUSD_LUX_FLOAT_SPECULAR:
                return SetLuxAttribute(
                    light.CreateSpecularAttr(), value, "light specular multiplier", error);
            case OPENUSD_LUX_FLOAT_COLOR_TEMPERATURE:
                return SetLuxAttribute(
                    light.CreateColorTemperatureAttr(), value, "light color temperature", error);
            default:
                WriteError(error, "The requested light float property is unsupported.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

    });
}

openusd_status openusd_lux_get_float(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_float_property property,
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
            WriteError(error, "A light float output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &light, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        switch (property)
        {
            case OPENUSD_LUX_FLOAT_INTENSITY:
                return GetLuxAttribute(light.GetIntensityAttr(), value, "light intensity", error);
            case OPENUSD_LUX_FLOAT_EXPOSURE:
                return GetLuxAttribute(light.GetExposureAttr(), value, "light exposure", error);
            case OPENUSD_LUX_FLOAT_DIFFUSE:
                return GetLuxAttribute(light.GetDiffuseAttr(), value, "light diffuse multiplier", error);
            case OPENUSD_LUX_FLOAT_SPECULAR:
                return GetLuxAttribute(light.GetSpecularAttr(), value, "light specular multiplier", error);
            case OPENUSD_LUX_FLOAT_COLOR_TEMPERATURE:
                return GetLuxAttribute(
                    light.GetColorTemperatureAttr(), value, "light color temperature", error);
            default:
                WriteError(error, "The requested light float property is unsupported.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

    });
}

openusd_status openusd_lux_set_bool(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_bool_property property,
    int32_t value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &light, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        switch (property)
        {
            case OPENUSD_LUX_BOOL_ENABLE_COLOR_TEMPERATURE:
                return SetLuxAttribute(
                    light.CreateEnableColorTemperatureAttr(),
                    value != 0,
                    "enable color temperature",
                    error);
            case OPENUSD_LUX_BOOL_NORMALIZE:
                return SetLuxAttribute(light.CreateNormalizeAttr(), value != 0, "light normalize", error);
            default:
                WriteError(error, "The requested light bool property is unsupported.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

    });
}

openusd_status openusd_lux_get_bool(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_bool_property property,
    int32_t* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (value == nullptr)
        {
            WriteError(error, "A light bool output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &light, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        bool nativeValue = false;
        openusd_status readStatus;
        switch (property)
        {
            case OPENUSD_LUX_BOOL_ENABLE_COLOR_TEMPERATURE:
                readStatus = GetLuxAttribute(
                    light.GetEnableColorTemperatureAttr(),
                    &nativeValue,
                    "enable color temperature",
                    error);
                break;
            case OPENUSD_LUX_BOOL_NORMALIZE:
                readStatus = GetLuxAttribute(
                    light.GetNormalizeAttr(), &nativeValue, "light normalize", error);
                break;
            default:
                WriteError(error, "The requested light bool property is unsupported.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (readStatus == OPENUSD_STATUS_OK)
        {
            *value = nativeValue ? 1 : 0;
        }
        return readStatus;

    });
}

openusd_status openusd_lux_set_color(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_vec3f value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!std::isfinite(value.x) || !std::isfinite(value.y) || !std::isfinite(value.z))
        {
            WriteError(error, "A finite light color is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &light, error);
        return status == OPENUSD_STATUS_OK
            ? SetLuxAttribute(
                light.CreateColorAttr(), GfVec3f(value.x, value.y, value.z), "light color", error)
            : status;

    });
}

openusd_status openusd_lux_get_color(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_vec3f* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (value == nullptr)
        {
            WriteError(error, "A light color output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &light, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        GfVec3f nativeValue;
        const openusd_status readStatus =
            GetLuxAttribute(light.GetColorAttr(), &nativeValue, "light color", error);
        if (readStatus == OPENUSD_STATUS_OK)
        {
            value->x = nativeValue[0];
            value->y = nativeValue[1];
            value->z = nativeValue[2];
        }
        return readStatus;

    });
}

openusd_status openusd_lux_set_shape(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_shape_property property,
    float value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!std::isfinite(value))
        {
            WriteError(error, "A finite light shape value is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (property == OPENUSD_LUX_SHAPE_ANGLE &&
            (value < 0.0F || value >= 360.0F))
        {
            WriteError(error, "Distant-light angle must be at least 0 and less than 360 degrees.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (property != OPENUSD_LUX_SHAPE_ANGLE && value < 0.0F)
        {
            WriteError(error, "Light dimensions and radii must be non-negative.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        UsdPrim prim;
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &prim, &light, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        const UsdAttribute attribute = GetLuxShapeAttribute(prim, property, true, error);
        return attribute
            ? SetLuxAttribute(attribute, value, "light shape property", error)
            : OPENUSD_STATUS_INVALID_ARGUMENT;

    });
}

openusd_status openusd_lux_get_shape(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_shape_property property,
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
            WriteError(error, "A light shape output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        UsdPrim prim;
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &prim, &light, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        const UsdAttribute attribute = GetLuxShapeAttribute(prim, property, false, error);
        return attribute
            ? GetLuxAttribute(attribute, value, "light shape property", error)
            : OPENUSD_STATUS_INVALID_ARGUMENT;

    });
}

openusd_status openusd_lux_set_asset(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_asset_property property,
    const char* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (value == nullptr)
        {
            WriteError(error, "A light asset path is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        UsdPrim prim;
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &prim, &light, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        const UsdAttribute attribute = GetLuxTextureAttribute(prim, property, true, error);
        return attribute
            ? SetLuxAttribute(attribute, SdfAssetPath(value), "light texture asset", error)
            : OPENUSD_STATUS_INVALID_ARGUMENT;

    });
}

openusd_status openusd_lux_get_asset(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_asset_property property,
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
        if (required == nullptr)
        {
            WriteError(error, "A light asset size output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        UsdPrim prim;
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &prim, &light, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        const UsdAttribute attribute = GetLuxTextureAttribute(prim, property, false, error);
        if (!attribute)
        {
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        SdfAssetPath value;
        const openusd_status readStatus =
            GetLuxAttribute(attribute, &value, "light texture asset", error);
        return readStatus == OPENUSD_STATUS_OK
            ? CopyString(value.GetAssetPath(), buffer, capacity, required)
            : readStatus;

    });
}

openusd_status openusd_lux_has_shaping(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* has_shaping,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(has_shaping);
        if (has_shaping == nullptr)
        {
            WriteError(error, "A shaping result output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        UsdPrim prim;
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &prim, &light, error);
        if (status == OPENUSD_STATUS_OK)
        {
            *has_shaping = prim.HasAPI<UsdLuxShapingAPI>() ? 1 : 0;
        }
        return status;

    });
}

openusd_status openusd_lux_apply_shaping(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        UsdPrim prim;
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &prim, &light, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        if (prim.HasAPI<UsdLuxShapingAPI>())
        {
            return OPENUSD_STATUS_OK;
        }
        std::string whyNot;
        if (!UsdLuxShapingAPI::CanApply(prim, &whyNot))
        {
            WriteError(error, whyNot.empty() ? "UsdLuxShapingAPI cannot be applied." : whyNot);
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (!UsdLuxShapingAPI::Apply(prim))
        {
            WriteError(error, "Could not apply UsdLuxShapingAPI.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_lux_set_shaping(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_shaping_property property,
    float value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!std::isfinite(value))
        {
            WriteError(error, "A finite shaping value is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (property == OPENUSD_LUX_SHAPING_FOCUS && value < 0.0F)
        {
            WriteError(error, "Light shaping focus must be non-negative.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (property == OPENUSD_LUX_SHAPING_CONE_ANGLE &&
            (value < 0.0F || value > 180.0F))
        {
            WriteError(error, "Light shaping cone angle must be between 0 and 180 degrees.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (property == OPENUSD_LUX_SHAPING_CONE_SOFTNESS &&
            (value < 0.0F || value > 1.0F))
        {
            WriteError(error, "Light shaping cone softness must be between 0 and 1.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        UsdPrim prim;
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &prim, &light, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        const UsdLuxShapingAPI shaping(prim);
        if (!shaping)
        {
            WriteError(error, "UsdLuxShapingAPI has not been applied to the light.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdAttribute attribute = GetLuxShapingAttribute(shaping, property, true, error);
        return attribute
            ? SetLuxAttribute(attribute, value, "light shaping property", error)
            : OPENUSD_STATUS_INVALID_ARGUMENT;

    });
}

openusd_status openusd_lux_get_shaping(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_shaping_property property,
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
            WriteError(error, "A shaping value output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        UsdPrim prim;
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &prim, &light, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        const UsdLuxShapingAPI shaping(prim);
        if (!shaping)
        {
            WriteError(error, "UsdLuxShapingAPI has not been applied to the light.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdAttribute attribute = GetLuxShapingAttribute(shaping, property, false, error);
        return attribute
            ? GetLuxAttribute(attribute, value, "light shaping property", error)
            : OPENUSD_STATUS_INVALID_ARGUMENT;

    });
}

openusd_status openusd_skel_is_schema(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_schema_kind schema_kind,
    int32_t* is_schema,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(is_schema);
        if (is_schema == nullptr || schema_kind < OPENUSD_SKEL_SCHEMA_ROOT ||
            schema_kind > OPENUSD_SKEL_SCHEMA_ANIMATION)
        {
            WriteError(error, "A valid skeleton schema kind and result are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            const openusd_status status = GetSkelPrim(stage, prim_path, &prim, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            *is_schema = IsSkelSchema(prim, schema_kind) ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_skel_define(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_schema_kind schema_kind,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            schema_kind < OPENUSD_SKEL_SCHEMA_ROOT ||
            schema_kind > OPENUSD_SKEL_SCHEMA_ANIMATION)
        {
            WriteError(error, "A valid stage, absolute prim path, and skeleton schema kind are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            const SdfPath path(prim_path);
            UsdPrim prim;
            switch (schema_kind)
            {
                case OPENUSD_SKEL_SCHEMA_ROOT:
                    prim = UsdSkelRoot::Define(stage->value, path).GetPrim();
                    break;
                case OPENUSD_SKEL_SCHEMA_SKELETON:
                    prim = UsdSkelSkeleton::Define(stage->value, path).GetPrim();
                    break;
                case OPENUSD_SKEL_SCHEMA_ANIMATION:
                    prim = UsdSkelAnimation::Define(stage->value, path).GetPrim();
                    break;
                default:
                    break;
            }
            if (!prim || !IsSkelSchema(prim, schema_kind))
            {
                WriteError(error, "Could not define the requested UsdSkel schema.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_skel_has_binding(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* has_binding,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(has_binding);
        if (has_binding == nullptr)
        {
            WriteError(error, "A skeleton binding result is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            const openusd_status status = GetSkelPrim(stage, prim_path, &prim, error);
            if (status == OPENUSD_STATUS_OK)
            {
                *has_binding = prim.HasAPI<UsdSkelBindingAPI>() ? 1 : 0;
            }
            return status;
        });

    });
}

openusd_status openusd_skel_apply_binding(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        return Guard(error, [&]()
        {
            UsdPrim prim;
            const openusd_status status = GetSkelPrim(stage, prim_path, &prim, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            if (prim.HasAPI<UsdSkelBindingAPI>())
            {
                return OPENUSD_STATUS_OK;
            }
            std::string whyNot;
            if (!UsdSkelBindingAPI::CanApply(prim, &whyNot))
            {
                WriteError(
                    error,
                    whyNot.empty() ? "UsdSkelBindingAPI cannot be applied." : whyNot);
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            if (!UsdSkelBindingAPI::Apply(prim))
            {
                WriteError(error, "Could not apply UsdSkelBindingAPI.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_skel_set_joints(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_schema_kind schema_kind,
    const openusd_string_list_view* joints,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (schema_kind != OPENUSD_SKEL_SCHEMA_SKELETON &&
            schema_kind != OPENUSD_SKEL_SCHEMA_ANIMATION)
        {
            WriteError(error, "Joints are supported only on Skeleton and SkelAnimation schemas.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            VtTokenArray values;
            openusd_status status = ReadSkelTokens(
                joints, schema_kind == OPENUSD_SKEL_SCHEMA_SKELETON, &values, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }

            if (schema_kind == OPENUSD_SKEL_SCHEMA_SKELETON)
            {
                UsdSkelSkeleton skeleton;
                status = GetSkelSkeleton(stage, prim_path, &skeleton, error);
                if (status != OPENUSD_STATUS_OK)
                {
                    return status;
                }
                status = ValidateAuthoredArrayCardinality<GfMatrix4d>(
                    skeleton.GetBindTransformsAttr(), values.size(), "bindTransforms", error);
                if (status != OPENUSD_STATUS_OK)
                {
                    return status;
                }
                status = ValidateAuthoredArrayCardinality<GfMatrix4d>(
                    skeleton.GetRestTransformsAttr(), values.size(), "restTransforms", error);
                return status == OPENUSD_STATUS_OK
                    ? SetLuxAttribute(skeleton.CreateJointsAttr(), values, "skeleton joints", error)
                    : status;
            }

            UsdSkelAnimation animation;
            status = GetSkelAnimation(stage, prim_path, &animation, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            status = ValidateAuthoredArrayCardinality<GfVec3f>(
                animation.GetTranslationsAttr(), values.size(), "translations", error);
            if (status == OPENUSD_STATUS_OK)
            {
                status = ValidateAuthoredArrayCardinality<GfQuatf>(
                    animation.GetRotationsAttr(), values.size(), "rotations", error);
            }
            if (status == OPENUSD_STATUS_OK)
            {
                status = ValidateAuthoredArrayCardinality<GfVec3h>(
                    animation.GetScalesAttr(), values.size(), "scales", error);
            }
            return status == OPENUSD_STATUS_OK
                ? SetLuxAttribute(animation.CreateJointsAttr(), values, "animation joints", error)
                : status;
        });

    });
}

openusd_status openusd_skel_get_joints(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_schema_kind schema_kind,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetStringListOutput(list, view);
        if ((schema_kind != OPENUSD_SKEL_SCHEMA_SKELETON &&
             schema_kind != OPENUSD_SKEL_SCHEMA_ANIMATION) ||
            list == nullptr || view == nullptr ||
            view->struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(error, "A supported schema and versioned joint-list outputs are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return GuardStringListOutput(error, list, view, [&](auto& result)
        {
            VtTokenArray joints;
            openusd_status status;
            if (schema_kind == OPENUSD_SKEL_SCHEMA_SKELETON)
            {
                UsdSkelSkeleton skeleton;
                status = GetSkelSkeleton(stage, prim_path, &skeleton, error);
                if (status == OPENUSD_STATUS_OK && !skeleton.GetJointsAttr().Get(&joints))
                {
                    WriteError(error, "The skeleton has no authored joints.");
                    status = OPENUSD_STATUS_NOT_FOUND;
                }
            }
            else
            {
                UsdSkelAnimation animation;
                status = GetSkelAnimation(stage, prim_path, &animation, error);
                if (status == OPENUSD_STATUS_OK && !animation.GetJointsAttr().Get(&joints))
                {
                    WriteError(error, "The animation has no authored joints.");
                    status = OPENUSD_STATUS_NOT_FOUND;
                }
            }
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            result = std::make_unique<openusd_string_list>();
            FillStringList(result.get(), ToStrings(joints), view);
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_skel_set_skeleton_matrices(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_matrix_property property,
    const openusd_matrix4d* values,
    size_t count,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!IsValidArrayBuffer(values, count))
        {
            WriteError(error, "An aligned matrix buffer and non-overflowing count are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        for (size_t index = 0; index < count; ++index)
        {
            if (!IsFiniteMatrix(values[index]))
            {
                WriteError(error, "Skeleton matrices must contain only finite values.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
        }
        return Guard(error, [&]()
        {
            UsdSkelSkeleton skeleton;
            openusd_status status = GetSkelSkeleton(stage, prim_path, &skeleton, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            size_t jointCount = 0;
            status = GetSkeletonJointCount(skeleton, &jointCount, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            if (count != jointCount)
            {
                WriteError(error, "Skeleton matrix cardinality must match joints.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            UsdAttribute attribute;
            if (property == OPENUSD_SKEL_MATRIX_BIND_TRANSFORMS)
            {
                attribute = skeleton.CreateBindTransformsAttr();
            }
            else if (property == OPENUSD_SKEL_MATRIX_REST_TRANSFORMS)
            {
                attribute = skeleton.CreateRestTransformsAttr();
            }
            else
            {
                WriteError(error, "The requested skeleton matrix property is unsupported.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            return SetSchemaArray<openusd_matrix4d, GfMatrix4d>(
                attribute,
                values,
                count,
                UsdTimeCode::Default(),
                SdfValueTypeNames->Matrix4dArray,
                "skeleton matrices",
                [](const openusd_matrix4d& value) { return ToMatrix4d(value); },
                error);
        });

    });
}

openusd_status openusd_skel_get_skeleton_matrices(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_matrix_property property,
    openusd_matrix4d* values,
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
                UsdSkelSkeleton skeleton;
                const openusd_status status =
                    GetSkelSkeleton(stage, prim_path, &skeleton, error);
                if (status != OPENUSD_STATUS_OK)
                {
                    return status;
                }
                UsdAttribute attribute;
                if (property == OPENUSD_SKEL_MATRIX_BIND_TRANSFORMS)
                {
                    attribute = skeleton.GetBindTransformsAttr();
                }
                else if (property == OPENUSD_SKEL_MATRIX_REST_TRANSFORMS)
                {
                    attribute = skeleton.GetRestTransformsAttr();
                }
                else
                {
                    WriteError(error, "The requested skeleton matrix property is unsupported.");
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
                }
                return GetSchemaArray<openusd_matrix4d, GfMatrix4d>(
                    attribute,
                    UsdTimeCode::Default(),
                    values,
                    capacity,
                    required,
                    SdfValueTypeNames->Matrix4dArray,
                    "skeleton matrices",
                    [](const GfMatrix4d& value) { return FromMatrix4d(value); },
                    error);
            });
        });

    });
}

openusd_status openusd_skel_set_animation_vec3(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_animation_vec3_property property,
    const openusd_vec3f* values,
    size_t count,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!IsValidArrayBuffer(values, count) ||
            (time_sampled != 0 && !std::isfinite(time_code)))
        {
            WriteError(error, "A valid animation vector buffer and finite time code are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        for (size_t index = 0; index < count; ++index)
        {
            if (!std::isfinite(values[index].x) ||
                !std::isfinite(values[index].y) ||
                !std::isfinite(values[index].z))
            {
                WriteError(error, "Animation vectors must contain only finite values.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            if (property == OPENUSD_SKEL_ANIMATION_SCALES &&
                (std::abs(values[index].x) > 65504.0F ||
                 std::abs(values[index].y) > 65504.0F ||
                 std::abs(values[index].z) > 65504.0F))
            {
                WriteError(error, "Animation scales must fit the half3 schema representation.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
        }
        return Guard(error, [&]()
        {
            UsdSkelAnimation animation;
            openusd_status status = GetSkelAnimation(stage, prim_path, &animation, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            size_t jointCount = 0;
            status = GetAnimationJointCount(animation, &jointCount, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            if (count != jointCount)
            {
                WriteError(error, "Animation vector cardinality must match joints.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            if (property == OPENUSD_SKEL_ANIMATION_TRANSLATIONS)
            {
                return SetSchemaArray<openusd_vec3f, GfVec3f>(
                    animation.CreateTranslationsAttr(),
                    values,
                    count,
                    GetTimeCode(time_sampled, time_code),
                    SdfValueTypeNames->Float3Array,
                    "animation translations",
                    [](openusd_vec3f value) { return GfVec3f(value.x, value.y, value.z); },
                    error);
            }
            if (property == OPENUSD_SKEL_ANIMATION_SCALES)
            {
                return SetSchemaArray<openusd_vec3f, GfVec3h>(
                    animation.CreateScalesAttr(),
                    values,
                    count,
                    GetTimeCode(time_sampled, time_code),
                    SdfValueTypeNames->Half3Array,
                    "animation scales",
                    [](openusd_vec3f value)
                    {
                        return GfVec3h(value.x, value.y, value.z);
                    },
                    error);
            }
            WriteError(error, "The requested animation vector property is unsupported.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        });

    });
}

openusd_status openusd_skel_get_animation_vec3(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_animation_vec3_property property,
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
            if (time_sampled != 0 && !std::isfinite(time_code))
            {
                WriteError(error, "A finite animation time code is required.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            return Guard(error, [&]()
            {
                UsdSkelAnimation animation;
                const openusd_status status =
                    GetSkelAnimation(stage, prim_path, &animation, error);
                if (status != OPENUSD_STATUS_OK)
                {
                    return status;
                }
                if (property == OPENUSD_SKEL_ANIMATION_TRANSLATIONS)
                {
                    return GetSchemaArray<openusd_vec3f, GfVec3f>(
                        animation.GetTranslationsAttr(),
                        GetTimeCode(time_sampled, time_code),
                        values,
                        capacity,
                        required,
                        SdfValueTypeNames->Float3Array,
                        "animation translations",
                        [](const GfVec3f& value)
                        {
                            return openusd_vec3f{value[0], value[1], value[2]};
                        },
                        error);
                }
                if (property == OPENUSD_SKEL_ANIMATION_SCALES)
                {
                    return GetSchemaArray<openusd_vec3f, GfVec3h>(
                        animation.GetScalesAttr(),
                        GetTimeCode(time_sampled, time_code),
                        values,
                        capacity,
                        required,
                        SdfValueTypeNames->Half3Array,
                        "animation scales",
                        [](const GfVec3h& value)
                        {
                            return openusd_vec3f{
                                static_cast<float>(value[0]),
                                static_cast<float>(value[1]),
                                static_cast<float>(value[2])};
                        },
                        error);
                }
                WriteError(error, "The requested animation vector property is unsupported.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            });
        });

    });
}

openusd_status openusd_skel_set_animation_rotations(
    const openusd_stage* stage,
    const char* prim_path,
    const openusd_quatf* values,
    size_t count,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!IsValidArrayBuffer(values, count) ||
            (time_sampled != 0 && !std::isfinite(time_code)))
        {
            WriteError(error, "A valid quaternion buffer and finite time code are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        for (size_t index = 0; index < count; ++index)
        {
            const openusd_quatf& value = values[index];
            const float lengthSquared =
                (value.real * value.real) + (value.x * value.x) +
                (value.y * value.y) + (value.z * value.z);
            if (!std::isfinite(lengthSquared) || std::abs(lengthSquared - 1.0F) > 0.002F)
            {
                WriteError(error, "Animation rotations must be finite unit quaternions.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
        }
        return Guard(error, [&]()
        {
            UsdSkelAnimation animation;
            openusd_status status = GetSkelAnimation(stage, prim_path, &animation, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            size_t jointCount = 0;
            status = GetAnimationJointCount(animation, &jointCount, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            if (count != jointCount)
            {
                WriteError(error, "Animation rotation cardinality must match joints.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            return SetSchemaArray<openusd_quatf, GfQuatf>(
                animation.CreateRotationsAttr(),
                values,
                count,
                GetTimeCode(time_sampled, time_code),
                SdfValueTypeNames->QuatfArray,
                "animation rotations",
                [](openusd_quatf value)
                {
                    return GfQuatf(value.real, GfVec3f(value.x, value.y, value.z));
                },
                error);
        });

    });
}

openusd_status openusd_skel_get_animation_rotations(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t time_sampled,
    double time_code,
    openusd_quatf* values,
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
            if (time_sampled != 0 && !std::isfinite(time_code))
            {
                WriteError(error, "A finite animation time code is required.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            return Guard(error, [&]()
            {
                UsdSkelAnimation animation;
                const openusd_status status =
                    GetSkelAnimation(stage, prim_path, &animation, error);
                if (status != OPENUSD_STATUS_OK)
                {
                    return status;
                }
                return GetSchemaArray<openusd_quatf, GfQuatf>(
                    animation.GetRotationsAttr(),
                    GetTimeCode(time_sampled, time_code),
                    values,
                    capacity,
                    required,
                    SdfValueTypeNames->QuatfArray,
                    "animation rotations",
                    [](const GfQuatf& value)
                    {
                        const GfVec3f imaginary = value.GetImaginary();
                        return openusd_quatf{
                            value.GetReal(), imaginary[0], imaginary[1], imaginary[2]};
                    },
                    error);
            });
        });

    });
}

openusd_status openusd_skel_set_binding_target(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_binding_relationship relationship,
    const char* target_prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!IsValidPrimPath(target_prim_path))
        {
            WriteError(error, "A valid absolute skeleton binding target path is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            UsdSkelBindingAPI binding;
            openusd_status status = GetSkelBinding(stage, prim_path, &prim, &binding, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            const SdfPath targetPath(target_prim_path);
            status = ValidateSkelBindingTarget(stage, relationship, targetPath, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            const UsdRelationship target =
                GetSkelBindingRelationship(binding, relationship, true, error);
            if (!target || !target.SetTargets(SdfPathVector{targetPath}))
            {
                WriteError(error, "Could not set the skeleton binding relationship target.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_skel_get_binding_target(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_binding_relationship relationship,
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
        if (required == nullptr)
        {
            WriteError(error, "A skeleton binding target size output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            UsdSkelBindingAPI binding;
            openusd_status status = GetSkelBinding(stage, prim_path, &prim, &binding, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            const UsdRelationship target =
                GetSkelBindingRelationship(binding, relationship, false, error);
            if (!target)
            {
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            SdfPathVector targets;
            if (!target.GetTargets(&targets) || targets.empty())
            {
                WriteError(error, "The skeleton binding relationship has no target.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (targets.size() != 1)
            {
                WriteError(error, "The skeleton binding relationship must have exactly one target.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            status = ValidateSkelBindingTarget(stage, relationship, targets[0], error);
            return status == OPENUSD_STATUS_OK
                ? CopyString(targets[0].GetString(), buffer, capacity, required)
                : status;
        });

    });
}

openusd_status openusd_skel_clear_binding_target(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_binding_relationship relationship,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        return Guard(error, [&]()
        {
            UsdPrim prim;
            UsdSkelBindingAPI binding;
            const openusd_status status = GetSkelBinding(stage, prim_path, &prim, &binding, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            const UsdRelationship target =
                GetSkelBindingRelationship(binding, relationship, false, error);
            if (!target)
            {
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            if (!target.ClearTargets(true))
            {
                WriteError(error, "Could not clear the skeleton binding relationship.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_skel_set_geom_bind_transform(
    const openusd_stage* stage,
    const char* prim_path,
    const openusd_matrix4d* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (value == nullptr || !IsAligned(value) || !IsFiniteMatrix(*value))
        {
            WriteError(error, "An aligned finite geometry bind transform is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            UsdSkelBindingAPI binding;
            const openusd_status status = GetSkelBinding(stage, prim_path, &prim, &binding, error);
            return status == OPENUSD_STATUS_OK
                ? SetLuxAttribute(
                    binding.CreateGeomBindTransformAttr(),
                    ToMatrix4d(*value),
                    "geometry bind transform",
                    error)
                : status;
        });

    });
}

openusd_status openusd_skel_get_geom_bind_transform(
    const openusd_stage* stage,
    const char* prim_path,
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
            WriteError(error, "An aligned geometry bind transform output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            UsdSkelBindingAPI binding;
            const openusd_status status = GetSkelBinding(stage, prim_path, &prim, &binding, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            GfMatrix4d matrix;
            const openusd_status readStatus = GetLuxAttribute(
                binding.GetGeomBindTransformAttr(), &matrix, "geometry bind transform", error);
            if (readStatus == OPENUSD_STATUS_OK)
            {
                *value = FromMatrix4d(matrix);
            }
            return readStatus;
        });

    });
}

openusd_status openusd_skel_set_joint_influences(
    const openusd_stage* stage,
    const char* prim_path,
    const int32_t* joint_indices,
    size_t joint_index_count,
    const float* joint_weights,
    size_t joint_weight_count,
    int32_t element_size,
    openusd_skel_interpolation interpolation,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!IsValidArrayBuffer(joint_indices, joint_index_count) ||
            !IsValidArrayBuffer(joint_weights, joint_weight_count) ||
            joint_index_count == 0 || joint_index_count != joint_weight_count ||
            element_size <= 0 ||
            joint_index_count % static_cast<size_t>(element_size) != 0 ||
            (interpolation != OPENUSD_SKEL_INTERPOLATION_CONSTANT &&
             interpolation != OPENUSD_SKEL_INTERPOLATION_VERTEX) ||
            (interpolation == OPENUSD_SKEL_INTERPOLATION_CONSTANT &&
             joint_index_count != static_cast<size_t>(element_size)))
        {
            WriteError(
                error,
                "Joint indices and weights must have equal non-zero tuple-shaped cardinality.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        for (size_t index = 0; index < joint_weight_count; ++index)
        {
            if (!std::isfinite(joint_weights[index]) || joint_weights[index] < 0.0F)
            {
                WriteError(error, "Joint weights must be finite and non-negative.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            UsdSkelBindingAPI binding;
            openusd_status status = GetSkelBinding(stage, prim_path, &prim, &binding, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            size_t jointCount = 0;
            status = GetBoundSkeletonJointCount(binding, &jointCount, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            std::vector<int> indices(joint_index_count);
            for (size_t index = 0; index < joint_index_count; ++index)
            {
                indices[index] = joint_indices[index];
            }
            std::string reason;
            if (!UsdSkelBindingAPI::ValidateJointIndices(
                    TfSpan<const int>(indices.data(), indices.size()), jointCount, &reason))
            {
                WriteError(error, reason.empty() ? "Joint indices are invalid." : reason);
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }

            const bool constant = interpolation == OPENUSD_SKEL_INTERPOLATION_CONSTANT;
            const UsdGeomPrimvar indicesPrimvar =
                binding.CreateJointIndicesPrimvar(constant, element_size);
            const UsdGeomPrimvar weightsPrimvar =
                binding.CreateJointWeightsPrimvar(constant, element_size);
            if (!indicesPrimvar || !weightsPrimvar)
            {
                WriteError(error, "Could not create skeleton influence primvars.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            VtIntArray indexValues(indices.begin(), indices.end());
            VtFloatArray weightValues(joint_weights, joint_weights + joint_weight_count);
            if (!indicesPrimvar.GetAttr().Set(indexValues) ||
                !weightsPrimvar.GetAttr().Set(weightValues))
            {
                WriteError(error, "Could not set skeleton influence primvars.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_skel_get_joint_influences(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* joint_indices,
    size_t joint_index_capacity,
    size_t* joint_index_required,
    float* joint_weights,
    size_t joint_weight_capacity,
    size_t* joint_weight_required,
    int32_t* element_size,
    openusd_skel_interpolation* interpolation,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(joint_index_required);
        ResetAbiOutput(joint_weight_required);
        ResetAbiOutput(element_size);
        ResetAbiOutput(interpolation);
        return WithAbiWritableBuffers(
            joint_indices,
            joint_index_capacity,
            joint_weights,
            joint_weight_capacity,
            [&]()
        {
            if (joint_index_required == nullptr || joint_weight_required == nullptr ||
                element_size == nullptr || interpolation == nullptr ||
                !IsValidArrayBuffer(joint_indices, joint_index_capacity) ||
                !IsValidArrayBuffer(joint_weights, joint_weight_capacity))
            {
                WriteError(error, "Valid influence buffers and metadata outputs are required.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            return Guard(error, [&]()
            {
                UsdPrim prim;
                UsdSkelBindingAPI binding;
                openusd_status status =
                    GetSkelBinding(stage, prim_path, &prim, &binding, error);
                if (status != OPENUSD_STATUS_OK)
                {
                    return status;
                }
                const UsdGeomPrimvar indicesPrimvar = binding.GetJointIndicesPrimvar();
                const UsdGeomPrimvar weightsPrimvar = binding.GetJointWeightsPrimvar();
                if (!indicesPrimvar || !weightsPrimvar)
                {
                    WriteError(error, "The prim has no authored joint influence primvars.");
                    return OPENUSD_STATUS_NOT_FOUND;
                }

                VtIntArray indexValues;
                VtFloatArray weightValues;
                if (!indicesPrimvar.GetAttr().Get(&indexValues) ||
                    !weightsPrimvar.GetAttr().Get(&weightValues))
                {
                    WriteError(error, "Could not read the joint influence primvars.");
                    return OPENUSD_STATUS_NOT_FOUND;
                }
                const int indexElementSize = indicesPrimvar.GetElementSize();
                const int weightElementSize = weightsPrimvar.GetElementSize();
                const TfToken indexInterpolation = indicesPrimvar.GetInterpolation();
                const TfToken weightInterpolation = weightsPrimvar.GetInterpolation();
                if (indexValues.empty() || indexValues.size() != weightValues.size() ||
                    indexElementSize <= 0 || indexElementSize != weightElementSize ||
                    indexValues.size() % static_cast<size_t>(indexElementSize) != 0 ||
                    indexInterpolation != weightInterpolation)
                {
                    WriteError(error, "The joint influence primvars have inconsistent shape.");
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
                }

                openusd_skel_interpolation outputInterpolation;
                if (indexInterpolation == UsdGeomTokens->constant)
                {
                    outputInterpolation = OPENUSD_SKEL_INTERPOLATION_CONSTANT;
                    if (indexValues.size() != static_cast<size_t>(indexElementSize))
                    {
                        WriteError(error, "Constant joint influences must contain one tuple.");
                        return OPENUSD_STATUS_INVALID_ARGUMENT;
                    }
                }
                else if (indexInterpolation == UsdGeomTokens->vertex)
                {
                    outputInterpolation = OPENUSD_SKEL_INTERPOLATION_VERTEX;
                }
                else
                {
                    WriteError(error, "Joint influences must use constant or vertex interpolation.");
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
                }

                size_t jointCount = 0;
                status = GetBoundSkeletonJointCount(binding, &jointCount, error);
                if (status != OPENUSD_STATUS_OK)
                {
                    return status;
                }
                std::string reason;
                if (!UsdSkelBindingAPI::ValidateJointIndices(
                        TfSpan<const int>(indexValues.data(), indexValues.size()),
                        jointCount,
                        &reason))
                {
                    WriteError(error, reason.empty() ? "Joint indices are invalid." : reason);
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
                }
                for (float weight : weightValues)
                {
                    if (!std::isfinite(weight) || weight < 0.0F)
                    {
                        WriteError(error, "Joint weights must be finite and non-negative.");
                        return OPENUSD_STATUS_INVALID_ARGUMENT;
                    }
                }

                const size_t indexCount = indexValues.size();
                const size_t weightCount = weightValues.size();
                if (joint_indices == nullptr && joint_index_capacity == 0 &&
                    joint_weights == nullptr && joint_weight_capacity == 0)
                {
                    *joint_index_required = indexCount;
                    *joint_weight_required = weightCount;
                    *element_size = indexElementSize;
                    *interpolation = outputInterpolation;
                    return OPENUSD_STATUS_OK;
                }
                if (joint_indices == nullptr || joint_weights == nullptr ||
                    joint_index_capacity < indexCount ||
                    joint_weight_capacity < weightCount)
                {
                    return OPENUSD_STATUS_BUFFER_TOO_SMALL;
                }
                for (size_t index = 0; index < indexCount; ++index)
                {
                    joint_indices[index] = indexValues[index];
                    joint_weights[index] = weightValues[index];
                }
                *joint_index_required = indexCount;
                *joint_weight_required = weightCount;
                *element_size = indexElementSize;
                *interpolation = outputInterpolation;
                return OPENUSD_STATUS_OK;
            });
        });

    });
}

void openusd_string_list_release(openusd_string_list* list)
{
    try
    {
        delete list;
    }
    catch (...)
    {
    }
}

void openusd_payload_arc_list_release(openusd_payload_arc_list* list)
{
    try
    {
        delete list;
    }
    catch (...)
    {
    }
}
