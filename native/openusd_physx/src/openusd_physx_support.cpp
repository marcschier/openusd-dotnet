// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_physx_support.h"

#include <algorithm>
#include <cmath>
#include <cstring>

namespace openusd_physx_support
{
void WriteError(openusd_physx_error_buffer* error, std::string_view message) noexcept
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
    if (count != 0)
    {
        std::memcpy(error->data, message.data(), count);
    }
    error->data[count] = '\0';
}

void ResetError(openusd_physx_error_buffer* error) noexcept
{
    if (error == nullptr)
    {
        return;
    }
    error->required = 0;
    if (error->data != nullptr && error->capacity != 0)
    {
        error->data[0] = '\0';
    }
}

openusd_physx_status CopyString(
    const std::string& value,
    char* buffer,
    size_t capacity,
    size_t* required) noexcept
{
    if (required == nullptr)
    {
        return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
    }
    *required = value.size() + 1;
    if (buffer == nullptr || capacity < *required)
    {
        return OPENUSD_PHYSX_STATUS_BUFFER_TOO_SMALL;
    }
    std::memcpy(buffer, value.c_str(), *required);
    return OPENUSD_PHYSX_STATUS_OK;
}

void CopyMessage(char* destination, size_t capacity, std::string_view message) noexcept
{
    if (destination == nullptr || capacity == 0)
    {
        return;
    }
    const size_t count = std::min(message.size(), capacity - 1);
    if (count != 0)
    {
        std::memcpy(destination, message.data(), count);
    }
    std::memset(destination + count, 0, capacity - count);
}

bool IsFinite(openusd_physx_vec3f value) noexcept
{
    return std::isfinite(value.x) && std::isfinite(value.y) && std::isfinite(value.z);
}

bool IsFinite(openusd_physx_quatf value) noexcept
{
    return std::isfinite(value.x) && std::isfinite(value.y) &&
        std::isfinite(value.z) && std::isfinite(value.w);
}

bool IsFinite(const openusd_physx_transform& value) noexcept
{
    return IsFinite(value.position) && IsFinite(value.rotation);
}

bool IsUsableRotation(openusd_physx_quatf value) noexcept
{
    if (!IsFinite(value))
    {
        return false;
    }
    const double length_squared =
        static_cast<double>(value.x) * static_cast<double>(value.x) +
        static_cast<double>(value.y) * static_cast<double>(value.y) +
        static_cast<double>(value.z) * static_cast<double>(value.z) +
        static_cast<double>(value.w) * static_cast<double>(value.w);
    return length_squared >= 0.25 && length_squared <= 4.0;
}

bool IsUnsetRotation(openusd_physx_quatf value) noexcept
{
    return value.x == 0.0F && value.y == 0.0F && value.z == 0.0F && value.w == 0.0F;
}

bool IsUnsetOrUsableRotation(openusd_physx_quatf value) noexcept
{
    return IsUnsetRotation(value) || IsUsableRotation(value);
}

openusd_physx_quatf ResolveRotationOrIdentity(openusd_physx_quatf value) noexcept
{
    const openusd_physx_quatf identity{0.0F, 0.0F, 0.0F, 1.0F};
    if (!IsUsableRotation(value))
    {
        return identity;
    }
    const double length = std::sqrt(
        static_cast<double>(value.x) * static_cast<double>(value.x) +
        static_cast<double>(value.y) * static_cast<double>(value.y) +
        static_cast<double>(value.z) * static_cast<double>(value.z) +
        static_cast<double>(value.w) * static_cast<double>(value.w));
    if (!(length > 0.0))
    {
        return identity;
    }
    openusd_physx_quatf result{};
    result.x = static_cast<float>(static_cast<double>(value.x) / length);
    result.y = static_cast<float>(static_cast<double>(value.y) / length);
    result.z = static_cast<float>(static_cast<double>(value.z) / length);
    result.w = static_cast<float>(static_cast<double>(value.w) / length);
    return IsFinite(result) ? result : identity;
}

bool IsValidMaterial(float static_friction, float dynamic_friction, float restitution) noexcept
{
    return std::isfinite(static_friction) && static_friction >= 0.0F &&
        std::isfinite(dynamic_friction) && dynamic_friction >= 0.0F &&
        std::isfinite(restitution) && restitution >= 0.0F && restitution <= 1.0F;
}

bool IsPositiveFinite(float value) noexcept
{
    return std::isfinite(value) && value > 0.0F;
}

bool IsNonNegativeFinite(float value) noexcept
{
    return std::isfinite(value) && value >= 0.0F;
}

uint64_t ComputeIdentity(
    const char* path,
    size_t path_length,
    uint32_t instance_domain,
    uint32_t instance_index) noexcept
{
    constexpr uint64_t offset_basis = 1469598103934665603ULL;
    constexpr uint64_t prime = 1099511628211ULL;
    uint64_t hash = offset_basis;
    for (size_t index = 0; index < path_length; ++index)
    {
        hash ^= static_cast<uint64_t>(static_cast<unsigned char>(path[index]));
        hash *= prime;
    }
    for (int shift = 0; shift < 32; shift += 8)
    {
        hash ^= static_cast<uint64_t>((instance_domain >> shift) & 0xFFU);
        hash *= prime;
    }
    for (int shift = 0; shift < 32; shift += 8)
    {
        hash ^= static_cast<uint64_t>((instance_index >> shift) & 0xFFU);
        hash *= prime;
    }
    return hash == OPENUSD_PHYSX_INVALID_ID ? prime : hash;
}

bool IsValidUtf8(const unsigned char* data, size_t length) noexcept
{
    if (data == nullptr)
    {
        return length == 0;
    }
    size_t index = 0;
    while (index < length)
    {
        const unsigned char lead = data[index];
        size_t continuation_count = 0;
        uint32_t code_point = 0;
        if (lead < 0x80U)
        {
            if (lead == 0U)
            {
                return false;
            }
            ++index;
            continue;
        }
        if ((lead & 0xE0U) == 0xC0U)
        {
            continuation_count = 1;
            code_point = lead & 0x1FU;
        }
        else if ((lead & 0xF0U) == 0xE0U)
        {
            continuation_count = 2;
            code_point = lead & 0x0FU;
        }
        else if ((lead & 0xF8U) == 0xF0U)
        {
            continuation_count = 3;
            code_point = lead & 0x07U;
        }
        else
        {
            return false;
        }
        if (continuation_count > length - index - 1)
        {
            return false;
        }
        for (size_t offset = 1; offset <= continuation_count; ++offset)
        {
            const unsigned char continuation = data[index + offset];
            if ((continuation & 0xC0U) != 0x80U)
            {
                return false;
            }
            code_point = (code_point << 6) | (continuation & 0x3FU);
        }
        if ((continuation_count == 1 && code_point < 0x80U) ||
            (continuation_count == 2 && code_point < 0x800U) ||
            (continuation_count == 3 && code_point < 0x10000U) ||
            code_point > 0x10FFFFU ||
            (code_point >= 0xD800U && code_point <= 0xDFFFU))
        {
            return false;
        }
        index += continuation_count + 1;
    }
    return true;
}
}
