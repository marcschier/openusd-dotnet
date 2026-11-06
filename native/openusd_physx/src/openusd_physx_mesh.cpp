// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_physx_mesh.h"
#include "openusd_physx_runtime.h"
#include "openusd_physx_support.h"
#include "openusd_physx_translate.h"

#include <VHACD.h>

#include <atomic>
#include <cstring>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <vector>

static_assert(sizeof(openusd_physx_mesh_input) == 40);
static_assert(sizeof(openusd_physx_mesh_hull) == 16);
static_assert(sizeof(openusd_physx_vec3f) == 12);

struct openusd_physx_mesh_preparation
{
    std::mutex operation;
    std::mutex cancellation;
    std::atomic<bool> cancelled{false};
    VHACD::IVHACD* active = nullptr;
    bool started = false;
    bool completed = false;
    std::vector<openusd_physx_mesh_hull> hulls;
    std::vector<openusd_physx_vec3f> points;
};

namespace
{
using openusd_physx_support::Guard;
using openusd_physx_support::IsFinite;
using openusd_physx_support::WriteError;

openusd_physx_status Fail(
    openusd_physx_error_buffer* error, openusd_physx_status status, const std::string& message)
{
    WriteError(error, message);
    return status;
}

bool InRange(uint32_t offset, uint32_t count, uint32_t capacity) noexcept
{
    return offset <= capacity && count <= capacity - offset;
}

uint32_t Defaulted(uint32_t value, uint32_t fallback) noexcept
{
    return value == 0 ? fallback : value;
}

struct ConvexRelease
{
    void operator()(physx::PxConvexMesh* mesh) const noexcept
    {
        if (mesh != nullptr)
        {
            const openusd_physx_runtime::FactoryLock lock;
            mesh->release();
        }
    }
};

class Decomposition final : public VHACD::IVHACD::IUserCallback
{
public:
    explicit Decomposition(openusd_physx_mesh_preparation& preparation)
        : preparation_(preparation), algorithm_(VHACD::CreateVHACD())
    {
        if (algorithm_ == nullptr)
        {
            throw std::runtime_error("CPU convex decomposition could not be created.");
        }
        const std::lock_guard<std::mutex> lock(preparation_.cancellation);
        preparation_.active = algorithm_;
    }

    ~Decomposition() override
    {
        {
            const std::lock_guard<std::mutex> lock(preparation_.cancellation);
            preparation_.active = nullptr;
        }
        algorithm_->Release();
    }

    Decomposition(const Decomposition&) = delete;
    Decomposition& operator=(const Decomposition&) = delete;

    VHACD::IVHACD& Algorithm() noexcept { return *algorithm_; }

    void Update(double, double, const char*, const char*) override
    {
        // Compute resets its own cancellation bit. Keep the request monotonic across that reset.
        if (preparation_.cancelled.load())
        {
            algorithm_->Cancel();
        }
    }

private:
    openusd_physx_mesh_preparation& preparation_;
    VHACD::IVHACD* algorithm_;
};

openusd_physx_status Validate(
    const openusd_physx_mesh_input* inputs, uint32_t inputCount,
    const openusd_physx_vec3f* points, uint32_t pointCount,
    const uint32_t* indices, uint32_t indexCount, openusd_physx_error_buffer* error)
{
    if (inputCount > OPENUSD_PHYSX_MESH_MAX_BATCH ||
        pointCount > OPENUSD_PHYSX_MESH_MAX_POINTS ||
        indexCount > OPENUSD_PHYSX_MESH_MAX_INDICES)
    {
        return Fail(error, OPENUSD_PHYSX_STATUS_CAPACITY_EXCEEDED,
            "Collider preparation input exceeds the fixed batch, point or index capacity.");
    }
    if (inputs == nullptr || inputCount == 0 || points == nullptr ||
        (indexCount != 0 && indices == nullptr))
    {
        return Fail(error, OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT,
            "Collider preparation requires a nonempty input batch and its declared buffers.");
    }
    for (uint32_t point = 0; point < pointCount; ++point)
    {
        if (!IsFinite(points[point]))
        {
            return Fail(error, OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT,
                "Collider preparation points must be finite.");
        }
    }
    for (uint32_t inputIndex = 0; inputIndex < inputCount; ++inputIndex)
    {
        const auto& input = inputs[inputIndex];
        const std::string prefix = "Collider preparation input " + std::to_string(inputIndex) + ": ";
        const uint32_t minimumPoints = input.approximation == OPENUSD_PHYSX_MESH_CONVEX_HULL ? 3u : 4u;
        if (input.point_count < minimumPoints ||
            !InRange(input.point_offset, input.point_count, pointCount) ||
            !InRange(input.index_offset, input.index_count, indexCount))
        {
            return Fail(error, OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT,
                prefix + "mesh ranges are invalid or contain too few points for the requested preparation.");
        }
        const auto& first = points[input.point_offset];
        bool hasExtent = false;
        for (uint32_t point = 1; point < input.point_count; ++point)
        {
            const auto& value = points[input.point_offset + point];
            if (value.x != first.x || value.y != first.y || value.z != first.z)
            {
                hasExtent = true;
                break;
            }
        }
        if (!hasExtent)
        {
            return Fail(error, OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT,
                prefix + "mesh points have no spatial extent.");
        }
        if (input.approximation == OPENUSD_PHYSX_MESH_CONVEX_HULL)
        {
            if (input.max_hulls != 0 || input.max_vertices != 0 || input.resolution != 0 ||
                input.volume_error_percent != 0)
            {
                return Fail(error, OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT,
                    prefix + "convexHull does not accept decomposition settings.");
            }
        }
        else if (input.approximation == OPENUSD_PHYSX_MESH_CONVEX_DECOMPOSITION)
        {
            if (input.index_count < 3 || input.index_count % 3 != 0)
            {
                return Fail(error, OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT,
                    prefix + "convexDecomposition requires whole triangles.");
            }
            if (input.max_hulls > OPENUSD_PHYSX_MESH_MAX_HULLS ||
                (input.max_vertices != 0 && (input.max_vertices < 4 ||
                    input.max_vertices > OPENUSD_PHYSX_MESH_MAX_VERTICES)) ||
                (input.resolution != 0 && (input.resolution < 10000 ||
                    input.resolution > OPENUSD_PHYSX_MESH_MAX_RESOLUTION)) ||
                !std::isfinite(input.volume_error_percent) ||
                input.volume_error_percent < 0 || input.volume_error_percent > 100)
            {
                return Fail(error, OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT,
                    prefix + "decomposition settings are outside the supported bounds.");
            }
        }
        else
        {
            return Fail(error, OPENUSD_PHYSX_STATUS_UNSUPPORTED,
                prefix + "the approximation is not convexHull or convexDecomposition.");
        }
        for (uint32_t index = 0; index < input.index_count; ++index)
        {
            if (indices[input.index_offset + index] >= input.point_count)
            {
                return Fail(error, OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT,
                    prefix + "a triangle index is outside its own points.");
            }
        }
    }
    return OPENUSD_PHYSX_STATUS_OK;
}

openusd_physx_status Prepare(
    openusd_physx_mesh_preparation& preparation,
    const openusd_physx_mesh_input* inputs, uint32_t inputCount,
    const openusd_physx_vec3f* points, const uint32_t* indices,
    openusd_physx_error_buffer* error)
{
    openusd_physx_runtime::Reference runtime;
    std::string reason;
    if (!runtime.Acquire(reason))
    {
        return Fail(error, OPENUSD_PHYSX_STATUS_NATIVE_ERROR, reason);
    }
    std::vector<openusd_physx_mesh_hull> hulls;
    std::vector<openusd_physx_vec3f> resultPoints;
    for (uint32_t inputIndex = 0; inputIndex < inputCount; ++inputIndex)
    {
        if (preparation.cancelled.load())
        {
            return Fail(error, OPENUSD_PHYSX_STATUS_CANCELLED, "Collider preparation was cancelled.");
        }
        const auto& input = inputs[inputIndex];
        const std::string prefix = "Collider preparation input " + std::to_string(inputIndex) + ": ";
        if (input.approximation == OPENUSD_PHYSX_MESH_CONVEX_HULL)
        {
            std::vector<physx::PxVec3> source(input.point_count);
            for (uint32_t point = 0; point < input.point_count; ++point)
            {
                const auto& value = points[input.point_offset + point];
                source[point] = physx::PxVec3(value.x, value.y, value.z);
            }
            std::unique_ptr<physx::PxConvexMesh, ConvexRelease> convex(
                openusd_physx_translate::CookConvexMesh(
                    openusd_physx_runtime::Physics(), openusd_physx_runtime::CookingParams(),
                    source.data(), source.size()));
            if (!convex)
            {
                return Fail(error, OPENUSD_PHYSX_STATUS_NATIVE_ERROR,
                    prefix + "convex hull cooking failed. " + openusd_physx_runtime::TakeLastError());
            }
            const uint32_t count = convex->getNbVertices();
            if (count < 4 || count > OPENUSD_PHYSX_MESH_MAX_VERTICES ||
                resultPoints.size() + count > OPENUSD_PHYSX_MESH_MAX_POINTS)
            {
                return Fail(error, OPENUSD_PHYSX_STATUS_CAPACITY_EXCEEDED,
                    prefix + "cooked hull exceeds the prepared geometry bounds.");
            }
            hulls.push_back({inputIndex, static_cast<uint32_t>(resultPoints.size()), count, 0});
            const physx::PxVec3* vertices = convex->getVertices();
            for (uint32_t point = 0; point < count; ++point)
            {
                openusd_physx_vec3f value{vertices[point].x, vertices[point].y, vertices[point].z};
                if (!IsFinite(value))
                {
                    return Fail(error, OPENUSD_PHYSX_STATUS_NATIVE_ERROR,
                        prefix + "convex cooking produced a nonfinite point.");
                }
                resultPoints.push_back(value);
            }
            continue;
        }
        std::vector<float> source(static_cast<size_t>(input.point_count) * 3);
        for (uint32_t point = 0; point < input.point_count; ++point)
        {
            const auto& value = points[input.point_offset + point];
            source[static_cast<size_t>(point) * 3] = value.x;
            source[static_cast<size_t>(point) * 3 + 1] = value.y;
            source[static_cast<size_t>(point) * 3 + 2] = value.z;
        }
        Decomposition decomposition(preparation);
        VHACD::IVHACD::Parameters parameters;
        parameters.m_callback = &decomposition;
        parameters.m_asyncACD = false;
        parameters.m_maxConvexHulls = Defaulted(input.max_hulls, 64);
        parameters.m_maxNumVerticesPerCH = Defaulted(input.max_vertices, 64);
        parameters.m_resolution = Defaulted(input.resolution, 400000);
        parameters.m_minimumVolumePercentErrorAllowed =
            input.volume_error_percent == 0 ? 1 : input.volume_error_percent;
        bool computed = decomposition.Algorithm().Compute(
            source.data(), input.point_count, indices + input.index_offset,
            input.index_count / 3, parameters);
        if (preparation.cancelled.load())
        {
            return Fail(error, OPENUSD_PHYSX_STATUS_CANCELLED, "Collider preparation was cancelled.");
        }
        const uint32_t count = decomposition.Algorithm().GetNConvexHulls();
        if (!computed || count == 0 || count > parameters.m_maxConvexHulls)
        {
            return Fail(error, OPENUSD_PHYSX_STATUS_NATIVE_ERROR,
                prefix + "convex decomposition did not produce a complete bounded hull set.");
        }
        for (uint32_t hull = 0; hull < count; ++hull)
        {
            VHACD::IVHACD::ConvexHull value;
            if (!decomposition.Algorithm().GetConvexHull(hull, value) ||
                value.m_points.size() < 4 ||
                value.m_points.size() > parameters.m_maxNumVerticesPerCH ||
                !std::isfinite(value.m_volume))
            {
                return Fail(error, OPENUSD_PHYSX_STATUS_NATIVE_ERROR,
                    prefix + "decomposition produced unusable hull " + std::to_string(hull) +
                    " with " + std::to_string(value.m_points.size()) + " vertices (limit " +
                    std::to_string(parameters.m_maxNumVerticesPerCH) + ") and volume " +
                    std::to_string(value.m_volume) + ".");
            }
            if (resultPoints.size() + value.m_points.size() > OPENUSD_PHYSX_MESH_MAX_POINTS)
            {
                return Fail(error, OPENUSD_PHYSX_STATUS_CAPACITY_EXCEEDED,
                    prefix + "prepared points exceed the batch output capacity.");
            }
            hulls.push_back({inputIndex, static_cast<uint32_t>(resultPoints.size()),
                static_cast<uint32_t>(value.m_points.size()), 0});
            // V-HACD's signed diagnostic volume can round below zero for thin hulls.
            // Preserve every hull's points; the PhysX world cooker validates the actual geometry.
            for (const auto& vertex : value.m_points)
            {
                openusd_physx_vec3f point{
                    static_cast<float>(vertex.mX),
                    static_cast<float>(vertex.mY),
                    static_cast<float>(vertex.mZ)};
                if (!IsFinite(point))
                {
                    return Fail(error, OPENUSD_PHYSX_STATUS_NATIVE_ERROR,
                        prefix + "decomposition produced a point outside the finite float range.");
                }
                resultPoints.push_back(point);
            }
        }
    }
    if (preparation.cancelled.load())
    {
        return Fail(error, OPENUSD_PHYSX_STATUS_CANCELLED, "Collider preparation was cancelled.");
    }
    preparation.hulls = std::move(hulls);
    preparation.points = std::move(resultPoints);
    preparation.completed = true;
    return OPENUSD_PHYSX_STATUS_OK;
}
}

extern "C" OPENUSD_PHYSX_API openusd_physx_status openusd_physx_mesh_preparation_create(
    uint32_t abi_version, openusd_physx_mesh_preparation** preparation,
    openusd_physx_error_buffer* error)
{
    return Guard(error, [&]() {
        if (preparation == nullptr)
        {
            return Fail(error, OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT,
                "A collider preparation output handle is required.");
        }
        *preparation = nullptr;
        if (abi_version != OPENUSD_PHYSX_MESH_ABI_VERSION)
        {
            return Fail(error, OPENUSD_PHYSX_STATUS_VERSION_MISMATCH,
                "Collider preparation ABI version mismatch.");
        }
        *preparation = new openusd_physx_mesh_preparation();
        return OPENUSD_PHYSX_STATUS_OK;
    });
}

extern "C" OPENUSD_PHYSX_API void openusd_physx_mesh_preparation_release(
    openusd_physx_mesh_preparation* preparation)
{
    delete preparation;
}

extern "C" OPENUSD_PHYSX_API void openusd_physx_mesh_preparation_cancel(
    openusd_physx_mesh_preparation* preparation)
{
    if (preparation != nullptr)
    {
        preparation->cancelled.store(true);
        const std::lock_guard<std::mutex> lock(preparation->cancellation);
        if (preparation->active != nullptr)
        {
            preparation->active->Cancel();
        }
    }
}

extern "C" OPENUSD_PHYSX_API openusd_physx_status openusd_physx_mesh_preparation_compute(
    openusd_physx_mesh_preparation* preparation,
    const openusd_physx_mesh_input* inputs, uint32_t input_count,
    const openusd_physx_vec3f* points, uint32_t point_count,
    const uint32_t* indices, uint32_t index_count, openusd_physx_error_buffer* error)
{
    return Guard(error, [&]() {
        if (preparation == nullptr)
        {
            return Fail(error, OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT, "A preparation handle is required.");
        }
        const std::lock_guard<std::mutex> lock(preparation->operation);
        if (preparation->started)
        {
            return Fail(error, OPENUSD_PHYSX_STATUS_INVALID_STATE, "A preparation handle is one-shot.");
        }
        preparation->started = true;
        if (preparation->cancelled.load())
        {
            return Fail(error, OPENUSD_PHYSX_STATUS_CANCELLED, "Collider preparation was cancelled.");
        }
        auto status = Validate(inputs, input_count, points, point_count, indices, index_count, error);
        return status == OPENUSD_PHYSX_STATUS_OK
            ? Prepare(*preparation, inputs, input_count, points, indices, error)
            : status;
    });
}

extern "C" OPENUSD_PHYSX_API openusd_physx_status openusd_physx_mesh_preparation_copy(
    openusd_physx_mesh_preparation* preparation,
    openusd_physx_mesh_hull* hulls, uint32_t hull_capacity,
    openusd_physx_vec3f* points, uint32_t point_capacity,
    uint32_t* hull_count, uint32_t* point_count, openusd_physx_error_buffer* error)
{
    return Guard(error, [&]() {
        if (hull_count != nullptr) *hull_count = 0;
        if (point_count != nullptr) *point_count = 0;
        if (preparation == nullptr || hull_count == nullptr || point_count == nullptr ||
            (hull_capacity != 0 && hulls == nullptr) || (point_capacity != 0 && points == nullptr))
        {
            return Fail(error, OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT, "Prepared output arguments are invalid.");
        }
        const std::lock_guard<std::mutex> lock(preparation->operation);
        if (preparation->cancelled.load())
        {
            return Fail(error, OPENUSD_PHYSX_STATUS_CANCELLED, "Collider preparation was cancelled.");
        }
        if (!preparation->completed)
        {
            return Fail(error, OPENUSD_PHYSX_STATUS_INVALID_STATE, "No complete prepared output is available.");
        }
        *hull_count = static_cast<uint32_t>(preparation->hulls.size());
        *point_count = static_cast<uint32_t>(preparation->points.size());
        if (hull_capacity < *hull_count || point_capacity < *point_count)
        {
            return Fail(error, OPENUSD_PHYSX_STATUS_BUFFER_TOO_SMALL, "Prepared output buffers are too small.");
        }
        std::memcpy(hulls, preparation->hulls.data(), preparation->hulls.size() * sizeof(*hulls));
        std::memcpy(points, preparation->points.data(), preparation->points.size() * sizeof(*points));
        return OPENUSD_PHYSX_STATUS_OK;
    });
}
