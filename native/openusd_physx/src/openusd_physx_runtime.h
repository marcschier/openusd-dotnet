// Copyright (c) marcschier. Licensed under the MIT License.

// Process wide, thread safe, reference counted PhysX runtime. It owns the
// single PxFoundation, PxPhysics, extension registration, allocator, and error
// callback for the whole process. Worlds and legacy scenes hold references and
// own their own scenes, dispatchers, and cooked resources, so the runtime
// always outlives every object created from it.

#ifndef OPENUSD_PHYSX_RUNTIME_H
#define OPENUSD_PHYSX_RUNTIME_H

#include <PxPhysicsAPI.h>

#include <string>
#include <utility>

namespace openusd_physx_runtime
{
// Adds one reference to the process runtime, creating it if this is the first
// reference. Returns false and fills reason when creation fails.
bool Acquire(std::string& reason);

// Removes one reference. The PhysX SDK objects themselves stay resident for the
// lifetime of the process even when the count reaches zero, because PhysX
// allows exactly one PxFoundation per process and rejects a second
// PxCreateFoundation call after the first instance has been released. Tearing
// the SDK down when the last world goes away therefore breaks every later
// world in the same process, which a host that opens and closes stages
// repeatedly hits immediately.
void Release() noexcept;

physx::PxPhysics& Physics() noexcept;

physx::PxFoundation& Foundation() noexcept;

const physx::PxCookingParams& CookingParams() noexcept;

// Returns and clears the most recent message reported by the PhysX error
// callback on the calling thread so failures can be reported instead of
// silently ignored. The storage is thread local, so one thread can never
// consume, overwrite, or observe the message of another thread.
std::string TakeLastError();

// Serializes the shared PxPhysics factory: scene, material, shape, actor, and
// joint creation, mesh cooking, and the matching release calls. PhysX does not
// guarantee that concurrent factory calls against one PxPhysics instance are
// safe, and the legacy stage ABI, the primitive scene ABI, and every retained
// world share the single process instance.
//
// Lock ordering: the factory lock is the innermost lock. A caller may take it
// while it holds a per world lock, never the other way around, and the runtime
// lifetime lock used by Acquire and Release is never taken while it is held.
// Releasing an object therefore has to scope the factory lock so it is dropped
// before the owning runtime reference is destroyed. The lock is recursive so
// shared helpers such as mesh cooking can take it without knowing whether an
// outer build already did.
class FactoryLock final
{
public:
    FactoryLock() noexcept;
    ~FactoryLock();

    FactoryLock(const FactoryLock&) = delete;
    FactoryLock& operator=(const FactoryLock&) = delete;
    FactoryLock(FactoryLock&&) = delete;
    FactoryLock& operator=(FactoryLock&&) = delete;
};

// True while the calling thread holds the factory lock. Acquire and Release
// assert on this because taking the runtime lifetime lock underneath the
// factory lock would invert the documented ordering.
bool HoldsFactoryLock() noexcept;

// Scoped reference to the process runtime.
class Reference final
{
public:
    Reference() noexcept = default;

    Reference(const Reference&) = delete;
    Reference& operator=(const Reference&) = delete;

    Reference(Reference&& other) noexcept
        : acquired_(other.acquired_)
    {
        other.acquired_ = false;
    }

    Reference& operator=(Reference&& other) noexcept
    {
        if (this != &other)
        {
            Reset();
            acquired_ = other.acquired_;
            other.acquired_ = false;
        }
        return *this;
    }

    ~Reference()
    {
        Reset();
    }

    bool Acquire(std::string& reason)
    {
        if (acquired_)
        {
            return true;
        }
        acquired_ = openusd_physx_runtime::Acquire(reason);
        return acquired_;
    }

    bool IsAcquired() const noexcept
    {
        return acquired_;
    }

    void Reset() noexcept
    {
        if (acquired_)
        {
            acquired_ = false;
            openusd_physx_runtime::Release();
        }
    }

private:
    bool acquired_ = false;
};
}

#endif
