// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_physx_runtime.h"

#include <cassert>
#include <cstdio>
#include <exception>
#include <mutex>

namespace
{
using namespace physx;

// The PhysX error callback is shared by the whole process, so the message it
// reports is stored per thread. A failing operation therefore only ever reads
// the message produced by its own thread, and two threads that fail at the
// same time cannot consume or overwrite each other's message.
std::string& ThreadErrorSlot() noexcept
{
    thread_local std::string message;
    return message;
}

class RuntimeErrorCallback final : public PxErrorCallback
{
public:
    void reportError(PxErrorCode::Enum code, const char* message, const char* file, int line) override
    {
        static_cast<void>(code);
        static_cast<void>(file);
        static_cast<void>(line);
        try
        {
            ThreadErrorSlot().assign(message == nullptr ? "" : message);
        }
        catch (const std::exception&)
        {
            // A message that cannot be stored is dropped instead of escaping
            // into PhysX, which does not expect this callback to throw.
        }
    }

    // Returns the message reported on the calling thread, if any, and clears
    // the slot so a later operation cannot report a stale failure.
    static std::string Take()
    {
        std::string& slot = ThreadErrorSlot();
        std::string message;
        message.swap(slot);
        return message;
    }
};

struct RuntimeState
{
    std::mutex mutex;
    std::recursive_mutex factory_mutex;
    size_t reference_count = 0;
    PxDefaultAllocator allocator;
    RuntimeErrorCallback error_callback;
    PxFoundation* foundation = nullptr;
    PxPhysics* physics = nullptr;
    bool extensions = false;
    PxCookingParams cooking_params{PxTolerancesScale()};
};

RuntimeState& State()
{
    static RuntimeState state;
    return state;
}

// Recursion depth of the factory lock on the calling thread.
size_t& FactoryDepth() noexcept
{
    thread_local size_t depth = 0;
    return depth;
}

// The factory lock is the innermost lock, so taking the runtime lifetime lock
// underneath it would invert the documented ordering. The check is always
// compiled, not only in debug builds, because the inversion is a latent
// deadlock that is hard to reproduce once it ships.
void AssertLifetimeLockOrdering(const char* operation) noexcept
{
    if (FactoryDepth() == 0)
    {
        return;
    }
    std::fprintf(
        stderr,
        "openusd_physx: lock ordering violation, %s was called while this thread held the factory lock.\n",
        operation);
    assert(false && "the factory lock must be released before the runtime lifetime lock is taken");
}

void TeardownLocked(RuntimeState& state) noexcept
{
    if (state.extensions)
    {
        PxCloseExtensions();
        state.extensions = false;
    }
    if (state.physics != nullptr)
    {
        state.physics->release();
        state.physics = nullptr;
    }
    if (state.foundation != nullptr)
    {
        state.foundation->release();
        state.foundation = nullptr;
    }
}
}

namespace openusd_physx_runtime
{
bool Acquire(std::string& reason)
{
    if (HoldsFactoryLock())
    {
        AssertLifetimeLockOrdering("Acquire");
        reason =
            "Lock ordering violation: the shared factory lock must be released before the process runtime is acquired.";
        return false;
    }
    RuntimeState& state = State();
    std::lock_guard<std::mutex> lock(state.mutex);
    if (state.physics != nullptr)
    {
        // The SDK is created once per process and is kept alive after the last
        // reference goes away, so later acquisitions only take a reference.
        ++state.reference_count;
        return true;
    }

    state.foundation = PxCreateFoundation(PX_PHYSICS_VERSION, state.allocator, state.error_callback);
    if (state.foundation == nullptr)
    {
        reason = "PxCreateFoundation failed: " + RuntimeErrorCallback::Take();
        return false;
    }
    state.physics = PxCreatePhysics(PX_PHYSICS_VERSION, *state.foundation, PxTolerancesScale());
    if (state.physics == nullptr)
    {
        reason = "PxCreatePhysics failed: " + RuntimeErrorCallback::Take();
        TeardownLocked(state);
        return false;
    }
    if (!PxInitExtensions(*state.physics, nullptr))
    {
        reason = "PxInitExtensions failed: " + RuntimeErrorCallback::Take();
        TeardownLocked(state);
        return false;
    }
    state.extensions = true;
    state.cooking_params = PxCookingParams(state.physics->getTolerancesScale());
    state.reference_count = 1;
    return true;
}

void Release() noexcept
{
    AssertLifetimeLockOrdering("Release");
    RuntimeState& state = State();
    std::lock_guard<std::mutex> lock(state.mutex);
    if (state.reference_count == 0)
    {
        return;
    }
    --state.reference_count;

    // The SDK objects deliberately stay resident once the count reaches zero.
    // PhysX permits exactly one PxFoundation per process and reports
    // "Foundation object exists already" for a second PxCreateFoundation call
    // after the first instance was released, so releasing here would make every
    // world created after the last one was destroyed fail to build. Holding the
    // foundation, physics, and extensions costs a fixed, small amount of memory
    // until the process exits and keeps repeated open and close cycles working.
}

PxPhysics& Physics() noexcept
{
    return *State().physics;
}

PxFoundation& Foundation() noexcept
{
    return *State().foundation;
}

const PxCookingParams& CookingParams() noexcept
{
    return State().cooking_params;
}

std::string TakeLastError()
{
    return RuntimeErrorCallback::Take();
}

FactoryLock::FactoryLock() noexcept
{
    State().factory_mutex.lock();
    ++FactoryDepth();
}

FactoryLock::~FactoryLock()
{
    --FactoryDepth();
    State().factory_mutex.unlock();
}

bool HoldsFactoryLock() noexcept
{
    return FactoryDepth() != 0;
}
}
