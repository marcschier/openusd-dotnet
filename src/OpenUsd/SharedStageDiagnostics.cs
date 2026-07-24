// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd;

internal readonly record struct UsdStageSchedulerDiagnosticSnapshot(
    int ActiveChildren,
    long PropertyInvalidations,
    long TopologyInvalidations,
    long CompositionInvalidations,
    long FullInvalidations);

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
internal static class SharedStageManagedDiagnostics
{
    private static int _liveSchedulers;
    private static int _liveRenderSources;
    private static int _liveRenderLeases;

    internal static int LiveSchedulers => Volatile.Read(ref _liveSchedulers);

    internal static int LiveRenderSources => Volatile.Read(ref _liveRenderSources);

    internal static int LiveRenderLeases => Volatile.Read(ref _liveRenderLeases);

    internal static void SchedulerCreated() => Interlocked.Increment(ref _liveSchedulers);

    internal static void SchedulerDestroyed() => Interlocked.Decrement(ref _liveSchedulers);

    internal static void RenderSourceCreated() => Interlocked.Increment(ref _liveRenderSources);

    internal static void RenderSourceDestroyed() => Interlocked.Decrement(ref _liveRenderSources);

    internal static void RenderLeaseCreated() => Interlocked.Increment(ref _liveRenderLeases);

    internal static void RenderLeaseDestroyed() => Interlocked.Decrement(ref _liveRenderLeases);
}

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
internal static class SharedStageNativeDiagnostics
{
    internal static (long Live, long Peak) GetStageCoreCounts() =>
        OpenUsdNativeRuntime.GetStageCoreDiagnostics();

    internal static void ResetStageCorePeak() =>
        OpenUsdNativeRuntime.ResetStageCoreDiagnosticPeak();

    internal static void SetDisplayColor(
        UsdStage stage,
        string primPath,
        float red,
        float green,
        float blue) =>
        OpenUsdNativeRuntime.SetDiagnosticDisplayColor(
            stage.Native,
            primPath,
            red,
            green,
            blue);
}
