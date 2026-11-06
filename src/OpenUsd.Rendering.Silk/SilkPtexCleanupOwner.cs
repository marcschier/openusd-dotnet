// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

// Candidate-local bounded retirement owner. Successful slots are cleared only
// after Dispose returns; a throwing slot and all unvisited slots stay owned.
internal sealed class SilkPtexCleanupOwner
{
    private readonly object _gate = new();
    private readonly IDisposable?[] _resources = new IDisposable?[64];
    private readonly bool[] _beforePayload = new bool[64];
    private bool _releasing;
    private bool _ready;
    private int _count;

    internal static SilkPtexCleanupOwner Create(SilkCpuTextureBudget budget)
    {
        IDisposable reservation = budget.Reserve(checked(64ul * ((ulong)IntPtr.Size + sizeof(byte))));
        try
        {
            var owner = new SilkPtexCleanupOwner();
            owner.Own(reservation);
            return owner;
        }
        catch
        {
            reservation.Dispose();
            throw;
        }
    }

    internal T Own<T>(T resource, bool beforePayload = false) where T : class, IDisposable
    {
        lock (_gate)
        {
            if (_releasing || _ready)
            {
                throw new InvalidOperationException("The Ptex cleanup owner is not accepting resources.");
            }
            if (_count == _resources.Length)
            {
                throw new InvalidOperationException("The Ptex cleanup owner capacity was exceeded.");
            }
            _beforePayload[_count] = beforePayload;
            _resources[_count++] = resource;
            return resource;
        }
    }

    internal void Release(IDisposable resource)
    {
        lock (_gate)
        {
            if (_releasing)
            {
                throw new InvalidOperationException("Ptex cleanup cannot reenter.");
            }
            for (int index = 0; index < _count; index++)
            {
                if (ReferenceEquals(_resources[index], resource))
                {
                    ReleaseAt(index);
                    return;
                }
            }
        }
    }

    internal void Retire()
    {
        lock (_gate)
        {
            _ready = true;
        }
    }

    internal bool Drain()
    {
        lock (_gate)
        {
            if (_releasing)
            {
                throw new InvalidOperationException("Ptex cleanup cannot reenter.");
            }
            if (!_ready)
            {
                return false;
            }
            for (int pass = 0; pass < 2; pass++)
            {
                for (int index = _count - 1; index >= 0; index--)
                {
                    if (_resources[index] is not null && _beforePayload[index] == (pass == 0))
                    {
                        ReleaseAt(index);
                    }
                }
            }
            return true;
        }
    }

    private void ReleaseAt(int index)
    {
        _releasing = true;
        try
        {
            _resources[index]!.Dispose();
            _resources[index] = null;
        }
        finally
        {
            _releasing = false;
        }
    }
}
