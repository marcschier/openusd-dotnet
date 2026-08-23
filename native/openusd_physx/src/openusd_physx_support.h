// Copyright (c) marcschier. Licensed under the MIT License.

// Internal helpers shared by the legacy scene ABI, the retained world ABI, and
// the page validator. Nothing in this header depends on PhysX or OpenUSD so it
// can be compiled into contract-only test binaries.

#ifndef OPENUSD_PHYSX_SUPPORT_H
#define OPENUSD_PHYSX_SUPPORT_H

#include "openusd_physx.h"
#include "openusd_physx_world.h"

#include <cmath>
#include <cstddef>
#include <exception>
#include <string>
#include <string_view>

namespace openusd_physx_support
{
void WriteError(openusd_physx_error_buffer* error, std::string_view message) noexcept;

void ResetError(openusd_physx_error_buffer* error) noexcept;

openusd_physx_status CopyString(
    const std::string& value,
    char* buffer,
    size_t capacity,
    size_t* required) noexcept;

void CopyMessage(char* destination, size_t capacity, std::string_view message) noexcept;

bool IsFinite(openusd_physx_vec3f value) noexcept;

bool IsFinite(openusd_physx_quatf value) noexcept;

bool IsFinite(const openusd_physx_transform& value) noexcept;

// A rotation must be finite and close enough to unit length that PhysX can
// normalize it without changing the intended orientation.
bool IsUsableRotation(openusd_physx_quatf value) noexcept;

// A rotation that a description may leave unset. An all zero quaternion is what
// a default initialized description carries and stands for the identity frame.
bool IsUnsetRotation(openusd_physx_quatf value) noexcept;

// The rule the page validator applies to an optional rotation.
bool IsUnsetOrUsableRotation(openusd_physx_quatf value) noexcept;

// Resolves an optional rotation into the unit quaternion a consumer applies.
// An unset rotation and a rotation the page contract rejects both become the
// identity; every rotation the contract accepts keeps its orientation and is
// only normalized, so a legal quaternion that is not unit length is never
// replaced by the identity.
openusd_physx_quatf ResolveRotationOrIdentity(openusd_physx_quatf value) noexcept;

bool IsValidMaterial(float static_friction, float dynamic_friction, float restitution) noexcept;

bool IsPositiveFinite(float value) noexcept;

bool IsNonNegativeFinite(float value) noexcept;

// FNV-1a over the canonical prim path, the instance domain, and the instance
// index. OPENUSD_PHYSX_INVALID_ID is never returned.
uint64_t ComputeIdentity(
    const char* path,
    size_t path_length,
    uint32_t instance_domain,
    uint32_t instance_index) noexcept;

bool IsValidUtf8(const unsigned char* data, size_t length) noexcept;

template <typename TAction>
openusd_physx_status Guard(openusd_physx_error_buffer* error, TAction&& action) noexcept
{
    try
    {
        ResetError(error);
        return action();
    }
    catch (const std::bad_alloc&)
    {
        WriteError(error, "The physics runtime ran out of memory.");
        return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
    }
    catch (const std::exception& exception)
    {
        WriteError(error, exception.what());
        return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
    }
    catch (...)
    {
        WriteError(error, "Unknown native physics exception.");
        return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
    }
}
}

#endif
