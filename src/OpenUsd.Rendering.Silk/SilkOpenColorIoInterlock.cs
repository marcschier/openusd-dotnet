// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Serializes every operation that observes or depends on OpenColorIO's process-global
/// caches.
/// </summary>
/// <remarks>
/// <para>
/// OpenColorIO caches parsed configs, file transforms, and processors. Those caches are
/// what make repeated lookups cheap, and they are also what makes an externally edited
/// LUT invisible: the file is re-referenced but never re-read, so a config whose
/// <c>FileTransform</c> points at a LUT that changed on disk keeps producing the old
/// colour, with the old cache identity, indefinitely. Reading a fresh identity therefore
/// requires clearing those caches first.
/// </para>
/// <para>
/// Clearing is process-global and affects every OpenColorIO consumer in the process,
/// including any processor another thread is building right now. This interlock is the
/// seam that makes that safe: a clear never overlaps a build, and two builds never
/// straddle a clear. It is deliberately a single process-wide lock rather than a
/// per-config one, because the caches it guards are themselves process-wide.
/// </para>
/// </remarks>
internal static class SilkOpenColorIoInterlock
{
    private static readonly object Gate = new();
    private static ulong _clears;

    /// <summary>Gets the number of times the global caches have been cleared.</summary>
    internal static ulong Clears
    {
        get
        {
            lock (Gate)
            {
                return _clears;
            }
        }
    }

    /// <summary>
    /// Runs an operation that constructs or uses an OpenColorIO processor, excluded
    /// against cache clearing.
    /// </summary>
    internal static TResult Read<TResult>(Func<TResult> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (Gate)
        {
            return operation();
        }
    }

    /// <summary>
    /// Clears the global caches and then runs an observation that must reflect the
    /// current contents of disk, with no other OpenColorIO work interleaved.
    /// </summary>
    internal static TResult Refresh<TResult>(Func<TResult> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (Gate)
        {
            try
            {
                OpenUsdNativeRuntime.ClearOcioCaches();
                _clears++;
            }
            catch (OpenUsdNativeException)
            {
                // A runtime that cannot clear its caches still answers the observation;
                // it is simply allowed to answer from a cache. The observation itself
                // decides what that means, and the identity it returns is still compared
                // against the retained one.
            }
            catch (Exception exception) when (
                exception is DllNotFoundException or EntryPointNotFoundException or
                    BadImageFormatException)
            {
            }

            return operation();
        }
    }
}
