// Copyright (c) marcschier. Licensed under the MIT License.

using global::Silk.NET.Vulkan;

namespace OpenUsd.Rendering.Silk.Vulkan;

internal sealed unsafe class VulkanSilkReadbackRetirement(
    VulkanSilkGraphicsDevice owner,
    Vk api,
    Device device) : SilkPendingSubmissionRetirement, IDisposable
{
    private readonly object _gate = new();
    private IDisposable? _sourceLease;
    private IDisposable? _reservation;
    private global::Silk.NET.Vulkan.Buffer _buffer;
    private DeviceMemory _memory;
    private CommandPool _pool;
    private Fence _fence;
    private bool _dependentRegistered;
    private bool _submitted;
    private bool _completed;
    private bool _disposed;
    private bool _releasing;

    internal void SetSourceLease(IDisposable lease) => _sourceLease = lease;

    internal void SetDependentRegistered() => _dependentRegistered = true;

    internal void SetReservation(IDisposable? reservation) => _reservation = reservation;

    internal void SetBuffer(global::Silk.NET.Vulkan.Buffer buffer) => _buffer = buffer;

    internal void SetMemory(DeviceMemory memory) => _memory = memory;

    internal void SetCommandPool(CommandPool pool) => _pool = pool;

    internal void SetSubmission(Fence fence)
    {
        _fence = fence;
        _submitted = true;
    }

    internal void ObserveWaitResult(Result result)
    {
        if (result is Result.Success or Result.ErrorDeviceLost)
        {
            _completed = true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            CompleteDisposal();
        }
    }

    internal void DisposeAfterFailure(Exception failure)
    {
        try
        {
            Dispose();
        }
        catch (Exception cleanup)
        {
            throw new AggregateException(
                "Vulkan readback and resource retirement failed.",
                failure,
                cleanup);
        }
    }

    internal override void RetireAfterDeviceIdle()
    {
        lock (_gate)
        {
            _completed = true;
            CompleteDisposal();
        }
    }

    private void CompleteDisposal()
    {
        if (_disposed || _releasing)
        {
            return;
        }
        if (_submitted && !_completed)
        {
            owner.RetainPendingSubmission(this);
            return;
        }

        _releasing = true;
        try
        {
            if (_fence.Handle != 0)
            {
                api.DestroyFence(device, _fence, null);
                _fence = default;
            }
            if (_pool.Handle != 0)
            {
                api.DestroyCommandPool(device, _pool, null);
                _pool = default;
            }
            if (_buffer.Handle != 0)
            {
                api.DestroyBuffer(device, _buffer, null);
                _buffer = default;
            }
            if (_memory.Handle != 0)
            {
                api.FreeMemory(device, _memory, null);
                _memory = default;
            }
            SilkOwnedResourceLeases.Dispose(ref _reservation);
            SilkOwnedResourceLeases.Dispose(ref _sourceLease);
            if (_dependentRegistered)
            {
                owner.ReleaseDependentObject();
                _dependentRegistered = false;
            }
            _disposed = true;
            owner.RemovePendingSubmission(this);
        }
        catch
        {
            owner.RetainPendingSubmission(this);
            throw;
        }
        finally
        {
            _releasing = false;
        }
    }
}
