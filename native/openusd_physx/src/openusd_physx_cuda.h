// Copyright (c) marcschier. Licensed under the MIT License.

// Optional CUDA acceleration for the retained physics world.
//
// Everything here is optional at every level. The simulation SDK only exposes a
// CUDA context manager on the platforms it supports at all, the PhysXGpu module
// is loaded dynamically at runtime, and the device may still be missing, busy,
// or too old. None of those is an error: the context is probed once per
// process, the answer is cached, and a world that cannot reach a device builds
// and steps every CPU domain it carries while reporting one diagnostic per GPU
// object it had to skip.

#ifndef OPENUSD_PHYSX_CUDA_H
#define OPENUSD_PHYSX_CUDA_H

#include <PxPhysicsAPI.h>

#include <string>

namespace openusd_physx_cuda
{
// True when this library was compiled against a simulation SDK configuration
// that can address a CUDA device at all. It is false on every platform PhysX
// does not build GPU support for, which is what keeps macOS and 32 bit targets
// free of a runtime that could never succeed.
bool IsCompiledIn() noexcept;

// Returns the process wide CUDA context manager, creating and validating it on
// the first call. Returns nullptr and fills reason when no device can be
// reached; the reason is stable for the lifetime of the process because the
// probe result is cached, so a caller never pays for a repeated failed device
// enumeration and never reports two different explanations for the same
// machine.
//
// Ownership: the context manager is owned by the process, exactly like the
// PxFoundation and PxPhysics instances the runtime owns, because PhysX objects
// created from it outlive individual worlds and a second creation attempt after
// a release is not guaranteed to succeed.
physx::PxCudaContextManager* Acquire(std::string& reason);

// Name of the device the acquired context manager runs on. Empty until a
// successful Acquire.
const std::string& DeviceName() noexcept;

// Human readable reason the device is unavailable, or an empty string while it
// is available or has not been probed yet.
const std::string& UnavailableReason() noexcept;
}

#endif
