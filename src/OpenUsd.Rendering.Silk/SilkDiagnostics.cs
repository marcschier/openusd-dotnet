// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

internal static class SilkManagedDiagnostics
{
    private static int _liveGpuMeshes;
    private static int _liveGpuSceneResources;
    private static int _livePages;
    private static int _liveSessions;

    internal static int LiveGpuMeshes => Volatile.Read(ref _liveGpuMeshes);

    internal static int LiveGpuSceneResources => Volatile.Read(ref _liveGpuSceneResources);

    internal static int LivePages => Volatile.Read(ref _livePages);

    internal static int LiveSessions => Volatile.Read(ref _liveSessions);

    internal static void GpuMeshCreated() => Interlocked.Increment(ref _liveGpuMeshes);

    internal static void GpuMeshDestroyed() => Interlocked.Decrement(ref _liveGpuMeshes);

    internal static void GpuSceneCreated() => Interlocked.Increment(ref _liveGpuSceneResources);

    internal static void GpuSceneDestroyed() => Interlocked.Decrement(ref _liveGpuSceneResources);

    internal static void PageCreated() => Interlocked.Increment(ref _livePages);

    internal static void PageDestroyed() => Interlocked.Decrement(ref _livePages);

    internal static void SessionCreated() => Interlocked.Increment(ref _liveSessions);

    internal static void SessionDestroyed() => Interlocked.Decrement(ref _liveSessions);
}
