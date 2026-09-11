// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer;

internal sealed class ViewerProductResourceRegistry : IDisposable
{
    private readonly HashSet<Lease> _leases = [];

    internal int Count => _leases.Count;

    internal Lease CreateLease()
    {
        var lease = new Lease(this);
        _leases.Add(lease);
        return lease;
    }

    public void Dispose()
    {
        List<Exception>? failures = null;
        foreach (Lease lease in _leases.ToArray())
        {
            try
            {
                lease.Dispose();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }
        if (failures is not null)
        {
            throw new AggregateException("Product capture resources remain owned for cleanup retry.", failures);
        }
    }

    internal sealed class Lease(ViewerProductResourceRegistry registry) : IDisposable
    {
        private readonly List<IDisposable> _resources = [];
        private readonly List<IDisposable> _lifetimePins = [];
        private bool _closing;
        private bool _disposed;

        internal T Own<T>(T resource) where T : IDisposable
        {
            ArgumentNullException.ThrowIfNull(resource);
            ObjectDisposedException.ThrowIf(_closing, this);
            _resources.Add(resource);
            return resource;
        }

        internal T Pin<T>(T lifetime) where T : IDisposable
        {
            ArgumentNullException.ThrowIfNull(lifetime);
            ObjectDisposedException.ThrowIf(_closing, this);
            _lifetimePins.Add(lifetime);
            return lifetime;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _closing = true;
            List<Exception>? failures = null;
            DisposeResources(_resources, ref failures);
            if (_resources.Count == 0)
            {
                DisposeResources(_lifetimePins, ref failures);
            }
            if (failures is not null)
            {
                throw new AggregateException(
                    "Product capture cleanup failed; failed resources remain registered.", failures);
            }
            registry._leases.Remove(this);
            _disposed = true;
        }

        private static void DisposeResources(List<IDisposable> resources, ref List<Exception>? failures)
        {
            for (int index = resources.Count - 1; index >= 0; index--)
            {
                try
                {
                    resources[index].Dispose();
                    resources.RemoveAt(index);
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
            }
        }
    }
}
