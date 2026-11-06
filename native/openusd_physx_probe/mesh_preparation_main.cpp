// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_physx_mesh.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <iostream>
#include <limits>
#include <memory>
#include <stdexcept>
#include <thread>
#include <vector>

namespace
{
using Handle = std::unique_ptr<openusd_physx_mesh_preparation,
    decltype(&openusd_physx_mesh_preparation_release)>;

void Require(bool condition, const char* message)
{
    if (!condition) throw std::runtime_error(message);
}

Handle Create()
{
    openusd_physx_mesh_preparation* raw = nullptr;
    Require(openusd_physx_mesh_preparation_create(OPENUSD_PHYSX_MESH_ABI_VERSION, &raw, nullptr)
        == OPENUSD_PHYSX_STATUS_OK && raw != nullptr, "Preparation creation failed.");
    return Handle(raw, openusd_physx_mesh_preparation_release);
}

void AddCube(float center, std::vector<openusd_physx_vec3f>& points, std::vector<uint32_t>& indices)
{
    const uint32_t offset = static_cast<uint32_t>(points.size());
    for (uint32_t vertex = 0; vertex < 8; ++vertex)
    {
        points.push_back({
            center + ((vertex & 1) != 0 ? 0.5F : -0.5F),
            (vertex & 2) != 0 ? 0.5F : -0.5F,
            (vertex & 4) != 0 ? 0.5F : -0.5F});
    }
    const uint32_t triangles[] = {
        0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6,
        0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7,
        0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5};
    for (uint32_t vertex : triangles) indices.push_back(offset + vertex);
}

void VerifyBatchedGeometry()
{
    std::vector<openusd_physx_vec3f> source;
    std::vector<uint32_t> triangles;
    AddCube(-2, source, triangles);
    AddCube(2, source, triangles);
    openusd_physx_mesh_input inputs[2]{};
    inputs[0].approximation = OPENUSD_PHYSX_MESH_CONVEX_HULL;
    inputs[0].point_count = static_cast<uint32_t>(source.size());
    inputs[1] = inputs[0];
    inputs[1].approximation = OPENUSD_PHYSX_MESH_CONVEX_DECOMPOSITION;
    inputs[1].index_count = static_cast<uint32_t>(triangles.size());
    Handle preparation = Create();
    char text[1024]{};
    openusd_physx_error_buffer error{text, sizeof(text), 0};
    auto status = openusd_physx_mesh_preparation_compute(preparation.get(),
        inputs, 2, source.data(), static_cast<uint32_t>(source.size()),
        triangles.data(), static_cast<uint32_t>(triangles.size()), &error);
    if (status != OPENUSD_PHYSX_STATUS_OK) throw std::runtime_error(text);

    uint32_t hullCount = 0;
    uint32_t pointCount = 0;
    Require(openusd_physx_mesh_preparation_copy(preparation.get(), nullptr, 0, nullptr, 0,
        &hullCount, &pointCount, &error) == OPENUSD_PHYSX_STATUS_BUFFER_TOO_SMALL,
        "A size query must report complete output counts.");
    Require(hullCount >= 3 && hullCount <= 65 && pointCount >= 16,
        "Decomposition was replaced with a single hull.");
    std::vector<openusd_physx_mesh_hull> hulls(hullCount, {999, 999, 999, 999});
    std::vector<openusd_physx_vec3f> points(pointCount, {999, 999, 999});
    Require(openusd_physx_mesh_preparation_copy(preparation.get(), hulls.data(), hullCount,
        points.data(), pointCount - 1, &hullCount, &pointCount, &error)
        == OPENUSD_PHYSX_STATUS_BUFFER_TOO_SMALL, "A short point buffer must be refused.");
    Require(hulls.front().input_index == 999 && points.front().x == 999,
        "An undersized copy wrote partial output.");
    Require(openusd_physx_mesh_preparation_copy(preparation.get(), hulls.data(), hullCount,
        points.data(), pointCount, &hullCount, &pointCount, &error)
        == OPENUSD_PHYSX_STATUS_OK, "The complete result could not be copied.");
    uint32_t convexCount = 0;
    uint32_t decompositionCount = 0;
    bool left = false;
    bool right = false;
    for (const auto& hull : hulls)
    {
        Require(hull.reserved == 0 && hull.point_count >= 4 &&
            hull.point_offset <= pointCount && hull.point_count <= pointCount - hull.point_offset,
            "A prepared hull has an invalid range.");
        float minimum = std::numeric_limits<float>::max();
        float maximum = -minimum;
        for (uint32_t point = 0; point < hull.point_count; ++point)
        {
            const auto& value = points[hull.point_offset + point];
            Require(std::isfinite(value.x) && std::isfinite(value.y) && std::isfinite(value.z),
                "A prepared point is not finite.");
            minimum = std::min(minimum, value.x);
            maximum = std::max(maximum, value.x);
        }
        if (hull.input_index == 0)
        {
            ++convexCount;
            Require(minimum < -2.4F && maximum > 2.4F, "The convex hull lost source extrema.");
        }
        else
        {
            Require(hull.input_index == 1, "A hull lost its input identity.");
            ++decompositionCount;
            Require(maximum < -1.4F || minimum > 1.4F,
                "Decomposition filled the authored gap with a single bridging hull.");
            left = left || maximum < -1.4F;
            right = right || minimum > 1.4F;
        }
    }
    Require(convexCount == 1 && decompositionCount >= 2 && left && right,
        "Prepared geometry does not preserve both disconnected source volumes.");
    Require(openusd_physx_mesh_preparation_compute(preparation.get(), inputs, 2,
        source.data(), static_cast<uint32_t>(source.size()), triangles.data(),
        static_cast<uint32_t>(triangles.size()), &error) == OPENUSD_PHYSX_STATUS_INVALID_STATE,
        "A one-shot preparation accepted a second computation.");
}

void VerifyRefusals()
{
    openusd_physx_mesh_preparation* raw = nullptr;
    Require(openusd_physx_mesh_preparation_create(2, &raw, nullptr)
        == OPENUSD_PHYSX_STATUS_VERSION_MISMATCH && raw == nullptr, "ABI mismatch was accepted.");
    const openusd_physx_vec3f points[] = {{0, 0, 0}, {1, 0, 0}, {0, 1, 0}, {0, 0, 1}};
    openusd_physx_mesh_input input{};
    input.approximation = OPENUSD_PHYSX_MESH_CONVEX_DECOMPOSITION;
    input.point_count = 4;
    input.index_count = 3;
    const uint32_t invalidIndices[] = {0, 1, 4};
    Handle invalid = Create();
    Require(openusd_physx_mesh_preparation_compute(invalid.get(), &input, 1, points, 4,
        invalidIndices, 3, nullptr) == OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT,
        "Out-of-range topology was accepted.");
    uint32_t hullCount = 99;
    uint32_t pointCount = 99;
    Require(openusd_physx_mesh_preparation_copy(invalid.get(), nullptr, 0, nullptr, 0,
        &hullCount, &pointCount, nullptr) == OPENUSD_PHYSX_STATUS_INVALID_STATE &&
        hullCount == 0 && pointCount == 0, "Failed preparation exposed output.");
    Handle oversized = Create();
    Require(openusd_physx_mesh_preparation_compute(oversized.get(), &input,
        OPENUSD_PHYSX_MESH_MAX_BATCH + 1, nullptr, 0, nullptr, 0, nullptr)
        == OPENUSD_PHYSX_STATUS_CAPACITY_EXCEEDED, "Batch admission did not precede input reads.");
    Handle degenerate = Create();
    const openusd_physx_vec3f coincident[4]{};
    auto emptyHull = input;
    emptyHull.approximation = OPENUSD_PHYSX_MESH_CONVEX_HULL;
    emptyHull.index_count = 0;
    Require(openusd_physx_mesh_preparation_compute(degenerate.get(), &emptyHull, 1, coincident, 4,
        nullptr, 0, nullptr) == OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT,
        "Coincident points must not be replaced by an invented box or reach voxel normalization.");
    Handle cancelled = Create();
    openusd_physx_mesh_preparation_cancel(cancelled.get());
    Require(openusd_physx_mesh_preparation_compute(cancelled.get(), &input, 1, points, 4,
        invalidIndices, 3, nullptr) == OPENUSD_PHYSX_STATUS_CANCELLED,
        "A prior cancellation was lost.");
    Require(openusd_physx_mesh_preparation_copy(cancelled.get(), nullptr, 0, nullptr, 0,
        &hullCount, &pointCount, nullptr) == OPENUSD_PHYSX_STATUS_CANCELLED &&
        hullCount == 0 && pointCount == 0, "Cancelled preparation exposed output.");
}

void VerifyConcurrentCancellation()
{
    std::vector<openusd_physx_vec3f> points;
    std::vector<uint32_t> indices;
    AddCube(-2, points, indices);
    AddCube(2, points, indices);
    std::array<openusd_physx_mesh_input, OPENUSD_PHYSX_MESH_MAX_BATCH> inputs{};
    for (auto& input : inputs)
    {
        input.approximation = OPENUSD_PHYSX_MESH_CONVEX_DECOMPOSITION;
        input.point_count = static_cast<uint32_t>(points.size());
        input.index_count = static_cast<uint32_t>(indices.size());
        input.resolution = OPENUSD_PHYSX_MESH_MAX_RESOLUTION;
    }
    Handle preparation = Create();
    std::atomic<bool> entered{false};
    auto status = OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
    std::thread worker([&]() {
        entered.store(true);
        status = openusd_physx_mesh_preparation_compute(preparation.get(), inputs.data(),
            static_cast<uint32_t>(inputs.size()), points.data(), static_cast<uint32_t>(points.size()),
            indices.data(), static_cast<uint32_t>(indices.size()), nullptr);
    });
    while (!entered.load()) std::this_thread::yield();
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
    const auto cancelled = std::chrono::steady_clock::now();
    openusd_physx_mesh_preparation_cancel(preparation.get());
    worker.join();
    Require(status == OPENUSD_PHYSX_STATUS_CANCELLED, "The running batch did not consume cancellation.");
    Require(std::chrono::steady_clock::now() - cancelled < std::chrono::seconds(5),
        "The cancelled batch did not drain within five seconds.");
    uint32_t hullCount = 99;
    uint32_t pointCount = 99;
    Require(openusd_physx_mesh_preparation_copy(preparation.get(), nullptr, 0, nullptr, 0,
        &hullCount, &pointCount, nullptr) == OPENUSD_PHYSX_STATUS_CANCELLED &&
        hullCount == 0 && pointCount == 0, "A cancelled batch published partial hulls.");
}
}

int main()
{
    try
    {
        VerifyRefusals();
        VerifyBatchedGeometry();
        VerifyConcurrentCancellation();
        std::cout << "Batched convex/decomposition geometry, identity, gap, limits, cancellation and atomic copy passed.\n";
        return 0;
    }
    catch (const std::exception& exception)
    {
        std::cerr << exception.what() << '\n';
        return 1;
    }
}
