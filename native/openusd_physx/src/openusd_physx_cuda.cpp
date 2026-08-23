// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_physx_cuda.h"

#include "openusd_physx_runtime.h"

#include <mutex>

namespace
{
using namespace physx;

struct CudaState
{
    std::once_flag once;
    PxCudaContextManager* manager = nullptr;
    std::string device_name;
    std::string reason;
};

CudaState& State()
{
    static CudaState state;
    return state;
}

#if PX_SUPPORT_GPU_PHYSX
// Owns the process runtime reference the probe needs, but only when the calling
// thread does not already hold the shared factory lock.
//
// The runtime forbids taking its lifetime lock underneath the factory lock,
// because that inverts the documented ordering and is a latent deadlock. A
// caller that already holds the factory lock necessarily already owns a live
// runtime - that is what the lock protects - so the probe borrows it instead of
// taking a second reference.
class RuntimeBorrow final
{
public:
    RuntimeBorrow() = default;

    RuntimeBorrow(const RuntimeBorrow&) = delete;
    RuntimeBorrow& operator=(const RuntimeBorrow&) = delete;
    RuntimeBorrow(RuntimeBorrow&&) = delete;
    RuntimeBorrow& operator=(RuntimeBorrow&&) = delete;

    ~RuntimeBorrow()
    {
        if (owned_)
        {
            openusd_physx_runtime::Release();
        }
    }

    bool Acquire(std::string& reason)
    {
        if (openusd_physx_runtime::HoldsFactoryLock())
        {
            return true;
        }
        owned_ = openusd_physx_runtime::Acquire(reason);
        return owned_;
    }

    // Keeps the reference for the lifetime of the process, which is what a
    // successful probe does: the context manager it produced outlives every
    // world, exactly like the foundation it was created from.
    void Keep() noexcept
    {
        owned_ = false;
    }

private:
    bool owned_ = false;
};

// Creates the context manager and refuses anything that is not actually usable.
// The order matters: a context that reports itself invalid must never be handed
// to PhysX, and a device below the architecture the GPU solver needs would fail
// later, inside a scene creation, where the failure is far harder to attribute.
void InitializeCore(CudaState& state)
{
    RuntimeBorrow runtime;
    std::string acquire_reason;
    if (!runtime.Acquire(acquire_reason))
    {
        state.reason = "The process physics runtime is unavailable: " + acquire_reason;
        return;
    }

    PxCudaContextManagerDesc desc;
    desc.deviceOrdinal = -1;
    PxCudaContextManager* manager = nullptr;
    {
        // Creating the context manager loads the PhysXGpu module and touches the
        // shared foundation, so it is serialized with every other factory call.
        const openusd_physx_runtime::FactoryLock factory_lock;
        manager = PxCreateCudaContextManager(openusd_physx_runtime::Foundation(), desc, PxGetProfilerCallback());
    }
    if (manager == nullptr)
    {
        state.reason = "No CUDA context manager could be created. " + openusd_physx_runtime::TakeLastError();
        if (state.reason.size() > 0 && state.reason.back() == ' ')
        {
            state.reason.pop_back();
        }
        return;
    }

    const char* refusal = nullptr;
    if (!manager->contextIsValid())
    {
        refusal = "A CUDA context manager was created but reports no valid context, so no device is usable.";
    }
    else if (!manager->supportsArchSM50())
    {
        refusal = "The CUDA device does not support the compute architecture the GPU solver requires.";
    }
    else if (manager->getDeviceTotalMemBytes() == 0)
    {
        refusal = "The CUDA device reports no memory, so no simulation buffer could be reserved on it.";
    }
    if (refusal != nullptr)
    {
        state.reason = refusal;
        const openusd_physx_runtime::FactoryLock factory_lock;
        manager->release();
        return;
    }

    const char* name = manager->getDeviceName();
    state.device_name = name == nullptr ? std::string() : std::string(name);
    state.manager = manager;
    runtime.Keep();
}

// Probing for a device is the one operation that is expected to fail on a
// perfectly healthy machine, and the simulation SDK reports that failure into
// the same per thread error slot every other operation reads. Draining it here
// is what keeps a machine without a device from attributing the loader message
// to the next unrelated failure, which would make two identical builds report
// two different reasons.
void Initialize(CudaState& state)
{
    InitializeCore(state);
    static_cast<void>(openusd_physx_runtime::TakeLastError());
}
#else
void Initialize(CudaState& state)
{
    state.reason =
        "This build of the simulation SDK has no CUDA support, so the GPU domains cannot be simulated.";
}
#endif
}

namespace openusd_physx_cuda
{
bool IsCompiledIn() noexcept
{
#if PX_SUPPORT_GPU_PHYSX
    return true;
#else
    return false;
#endif
}

PxCudaContextManager* Acquire(std::string& reason)
{
    CudaState& state = State();
    std::call_once(state.once, [&state]() { Initialize(state); });
    if (state.manager == nullptr)
    {
        reason = state.reason;
    }
    return state.manager;
}

const std::string& DeviceName() noexcept
{
    return State().device_name;
}

const std::string& UnavailableReason() noexcept
{
    return State().reason;
}
}
