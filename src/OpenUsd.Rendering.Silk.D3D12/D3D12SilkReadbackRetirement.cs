// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using Silk.NET.Direct3D12;

namespace OpenUsd.Rendering.Silk.D3D12;

[SupportedOSPlatform("windows")]
internal sealed unsafe class D3D12SilkReadbackRetirement(
    D3D12SilkGraphicsDevice owner) : SilkPendingSubmissionRetirement, IDisposable
{
    private readonly object _gate = new();
    private IDisposable? _sourceLease;
    private IDisposable? _reservation;
    private ID3D12Resource* _readback;
    private ID3D12CommandAllocator* _allocator;
    private ID3D12GraphicsCommandList* _commands;
    private ID3D12Fence* _fence;
    private bool _dependentRegistered;
    private bool _submitted;
    private bool _completed;
    private bool _disposed;
    private bool _releasing;

    internal void SetSourceLease(IDisposable lease) => _sourceLease = lease;

    internal void SetDependentRegistered() => _dependentRegistered = true;

    internal void SetReservation(IDisposable? reservation) => _reservation = reservation;

    internal void SetReadback(ID3D12Resource* readback) => _readback = readback;

    internal void SetAllocator(ID3D12CommandAllocator* allocator) => _allocator = allocator;

    internal void SetCommands(ID3D12GraphicsCommandList* commands) => _commands = commands;

    internal void SetFence(ID3D12Fence* fence) => _fence = fence;

    internal void MarkSubmitted() => _submitted = true;

    internal void MarkCompleted() => _completed = true;

    public void Dispose()
    {
        lock (_gate)
        {
            CompleteDisposal();
        }
    }

    internal void DisposeAfterFailure(Exception failure)
    {
        Exception? recoveryFailure = null;
        if (_submitted && !_completed)
        {
            try
            {
                if (owner.IsDeviceRemoved() || owner.TryDrainReadbackSubmittedWork())
                {
                    _completed = true;
                }
            }
            catch (Exception recovery)
            {
                recoveryFailure = recovery;
            }
        }

        try
        {
            Dispose();
        }
        catch (Exception cleanup)
        {
            if (recoveryFailure is not null)
            {
                throw new AggregateException(
                    "D3D12 readback, recovery, and resource retirement failed.",
                    failure,
                    recoveryFailure,
                    cleanup);
            }
            throw new AggregateException(
                "D3D12 readback and resource retirement failed.",
                failure,
                cleanup);
        }
        if (recoveryFailure is not null)
        {
            throw new AggregateException(
                "D3D12 readback and recovery failed.",
                failure,
                recoveryFailure);
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
            D3D12SilkGraphicsDevice.Release(ref _fence);
            D3D12SilkGraphicsDevice.Release(ref _commands);
            D3D12SilkGraphicsDevice.Release(ref _allocator);
            D3D12SilkGraphicsDevice.Release(ref _readback);
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
