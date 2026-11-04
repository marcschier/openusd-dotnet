// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

internal static class SilkOwnedResourceLeases
{
    internal static void Dispose(ref IDisposable? resource)
    {
        resource?.Dispose();
        resource = null;
    }

    internal static void Dispose<T>(ref T?[]? resources, ref bool releasing, bool reverse = false) where T : class, IDisposable
    {
        T?[]? current = resources;
        if (current is null)
        {
            return;
        }
        lock (current)
        {
            if (resources is null)
            {
                return;
            }
            if (releasing)
            {
                throw new InvalidOperationException("Owned resource release cannot reenter its active retirement.");
            }
            releasing = true;
            try
            {
                for (int index = 0; index < current.Length; index++)
                {
                    int selected = reverse ? current.Length - 1 - index : index;
                    current[selected]?.Dispose();
                    current[selected] = null;
                }
                resources = null;
            }
            finally
            {
                releasing = false;
            }
        }
    }
}
