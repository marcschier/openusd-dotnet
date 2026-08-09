// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd.Rendering.Silk;

internal enum SharedStageSoakOperation
{
    Property,
    Topology,
    Composition,
    Read
}

internal sealed record SharedStageSoakOptions
{
    internal int EditCount { get; init; } = 12_500;

    internal TimeSpan MinimumDuration { get; init; }

    internal TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);

    internal string AssetPath { get; init; } = string.Empty;

    internal Action? RequestStormFrame { get; init; }

    internal Func<long>? GetStormFrameCount { get; init; }

    internal Func<CancellationToken, Task>? SimulateContextLossAsync { get; init; }

    internal Func<SharedStageRendererDiagnostics>? GetRendererDiagnostics { get; init; }

    internal Func<ISilkGraphicsDevice>? CreateGraphicsDevice { get; init; }

    internal Action<string>? ReportStatus { get; init; }

    internal required SharedStageBuildIdentity BuildIdentity { get; init; }

    internal SharedStageResourceSnapshot BaselineResources { get; init; }
}

internal readonly record struct SharedStageRendererDiagnostics(
    long PreLossFrames,
    long PostLossFrames,
    int FaultCount,
    long ManagedRenderers,
    long NativeRenderers,
    long PeakNativeRenderers,
    long AbandonedEngines,
    long ShutdownCompletions);

internal readonly record struct SharedStageMeshIdentity(
    ulong Id,
    string Path);

internal readonly record struct SharedStageResourceSnapshot(
    long NativeStageCores,
    long NativePeakStageCores,
    int ManagedSchedulers,
    int ManagedRenderSources,
    int ManagedRenderLeases,
    int SchedulerChildren,
    long ManagedStormRenderers,
    long NativeStormRenderers,
    long NativePeakStormRenderers,
    long AbandonedStormEngines,
    long ManagedSilkSessions,
    long NativeSilkSessions,
    long NativePeakSilkSessions,
    long ManagedSilkPages,
    long NativeSilkPages,
    long NativePeakSilkPages,
    long GpuSceneResources,
    long GpuMeshResources)
{
    internal SharedStageResourceSnapshot Max(SharedStageResourceSnapshot other) => new(
        Math.Max(NativeStageCores, other.NativeStageCores),
        Math.Max(NativePeakStageCores, other.NativePeakStageCores),
        Math.Max(ManagedSchedulers, other.ManagedSchedulers),
        Math.Max(ManagedRenderSources, other.ManagedRenderSources),
        Math.Max(ManagedRenderLeases, other.ManagedRenderLeases),
        Math.Max(SchedulerChildren, other.SchedulerChildren),
        Math.Max(ManagedStormRenderers, other.ManagedStormRenderers),
        Math.Max(NativeStormRenderers, other.NativeStormRenderers),
        Math.Max(NativePeakStormRenderers, other.NativePeakStormRenderers),
        Math.Max(AbandonedStormEngines, other.AbandonedStormEngines),
        Math.Max(ManagedSilkSessions, other.ManagedSilkSessions),
        Math.Max(NativeSilkSessions, other.NativeSilkSessions),
        Math.Max(NativePeakSilkSessions, other.NativePeakSilkSessions),
        Math.Max(ManagedSilkPages, other.ManagedSilkPages),
        Math.Max(NativeSilkPages, other.NativeSilkPages),
        Math.Max(NativePeakSilkPages, other.NativePeakSilkPages),
        Math.Max(GpuSceneResources, other.GpuSceneResources),
        Math.Max(GpuMeshResources, other.GpuMeshResources));
}

internal readonly record struct SharedStageMemoryCheckpoint(
    int OrderedOperation,
    int MutatingOperation,
    long ManagedRetainedBytes,
    long WorkingSetBytes,
    ulong ChangeSerial,
    ulong PageRevision,
    int MeshCount,
    SharedStageResourceSnapshot Resources);

internal sealed record SharedStageBuildIdentity
{
    internal const uint DataAbi = OpenUsdNativeContract.AbiVersion;
    internal const uint StormAbi = RenderNativeAbiVersions.StormAbi;
    internal const uint SilkSessionAbi = RenderNativeAbiVersions.SilkSessionAbi;
    internal const uint SilkPageAbi = SilkCommandParser.PageAbiVersion;

    internal required string SourceHash { get; init; }

    internal required string ExecutableHash { get; init; }

    internal required DateTimeOffset ExecutableTimestamp { get; init; }

    internal required string BuildHash { get; init; }

    internal static SharedStageBuildIdentity FromEnvironment()
    {
        string sourceHash = RequireEnvironment("OPENUSD_SOAK_SOURCE_HASH");
        string expectedExecutableHash =
            RequireEnvironment("OPENUSD_SOAK_EXECUTABLE_HASH");
        string expectedTimestamp =
            RequireEnvironment("OPENUSD_SOAK_EXECUTABLE_TIMESTAMP_UTC");
        string executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The soak executable path is unavailable.");
        string actualExecutableHash = Convert.ToHexString(
            SHA256.HashData(File.ReadAllBytes(executable)));
        DateTimeOffset actualTimestamp = File.GetLastWriteTimeUtc(executable);
        ValidateExact(
            "executable hash",
            expectedExecutableHash,
            actualExecutableHash);
        ValidateExact(
            "executable timestamp",
            expectedTimestamp,
            actualTimestamp.ToString("O", CultureInfo.InvariantCulture));
        if (OpenUsdNativeRuntime.AbiVersion != DataAbi)
        {
            throw new InvalidOperationException(
                $"The data ABI is {OpenUsdNativeRuntime.AbiVersion}, expected {DataAbi}.");
        }

        string buildHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join(
                "|",
                sourceHash,
                actualExecutableHash,
                actualTimestamp.ToString("O", CultureInfo.InvariantCulture),
                DataAbi,
                StormAbi,
                SilkSessionAbi,
                SilkPageAbi))));
        return new SharedStageBuildIdentity
        {
            SourceHash = sourceHash,
            ExecutableHash = actualExecutableHash,
            ExecutableTimestamp = actualTimestamp,
            BuildHash = buildHash
        };
    }

    internal static void ValidateExact(string name, string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The soak {name} is stale: expected {expected}, actual {actual}.");
        }
    }

    private static string RequireEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"The mandatory soak identity variable {name} is not set.");
}

internal sealed record SharedStageSoakResult
{
    internal DateTimeOffset StartedAt { get; init; }

    internal DateTimeOffset CompletedAt { get; init; }

    internal int OrderedOperations { get; init; }

    internal int MutatingOperations { get; init; }

    internal int PropertyOperations { get; init; }

    internal int TopologyOperations { get; init; }

    internal int CompositionOperations { get; init; }

    internal int ReadOperations { get; init; }

    internal int ChangeNotifications { get; init; }

    internal int CoalescedEditCount { get; init; }

    internal ulong InitialChangeSerial { get; init; }

    internal ulong FinalChangeSerial { get; init; }

    internal long SilkSyncPages { get; init; }

    internal long SilkMeshUpserts { get; init; }

    internal long SilkMeshRemovals { get; init; }

    internal long SilkSteadyPages { get; init; }

    internal int PeakSilkMeshes { get; init; }

    internal int FinalSilkMeshes { get; init; }

    internal uint PeakPageCommands { get; init; }

    internal long StormFrames { get; init; }

    internal bool ContextLossSimulated { get; init; }

    internal bool SilkSessionTeardownSimulated { get; init; }

    internal bool ActiveChildRejectionObserved { get; init; }

    internal long ManagedAllocatedBytes { get; init; }

    internal long WarmManagedHeapBytes { get; init; }

    internal long FinalManagedHeapBytes { get; init; }

    internal long WarmWorkingSetBytes { get; init; }

    internal long FinalWorkingSetBytes { get; init; }

    internal int Gen0Collections { get; init; }

    internal int Gen1Collections { get; init; }

    internal int Gen2Collections { get; init; }

    internal required SharedStageBuildIdentity BuildIdentity { get; init; }

    internal SharedStageResourceSnapshot BaselineResources { get; init; }

    internal SharedStageResourceSnapshot PeakResources { get; init; }

    internal SharedStageResourceSnapshot FinalResources { get; init; }

    internal SharedStageMemoryCheckpoint[] MemoryCheckpoints { get; init; } = [];

    internal double ManagedRetainedSlopeBytesPerThousandEdits { get; init; }

    internal double WorkingSetSlopeBytesPerThousandEdits { get; init; }

    internal long PropertyInvalidations { get; init; }

    internal long TopologyInvalidations { get; init; }

    internal long CompositionInvalidations { get; init; }

    internal long FullInvalidations { get; init; }

    internal long PreLossStormFrames { get; init; }

    internal long PostLossStormFrames { get; init; }

    internal int RendererFaultCount { get; init; }

    internal bool TargetedColorUpsertObserved { get; init; }

    internal string FinalDisplayColorTime { get; init; } = string.Empty;

    internal float[] ExpectedFinalDisplayColor { get; init; } = [];

    internal float[] ActualFinalDisplayColor { get; init; } = [];

    internal SharedStageMeshIdentity[] ExpectedFinalMeshes { get; init; } = [];

    internal SharedStageMeshIdentity[] ActualFinalMeshes { get; init; } = [];

    internal ulong[] RemovedMeshIds { get; init; } = [];

    internal ulong[] RestoredMeshIds { get; init; } = [];

    internal string[] RemovedMeshPaths { get; init; } = [];

    internal string[] RestoredMeshPaths { get; init; } = [];

    internal long RendererShutdownCompletions { get; init; }

    internal bool ResourcesReleased { get; init; }

    internal SharedStageSoakResult WithResourcesReleased(
        SharedStageResourceSnapshot finalResources,
        SharedStageRendererDiagnostics finalRendererDiagnostics)
    {
        ValidateReclaimed(BaselineResources, finalResources, ContextLossSimulated);
        if (finalRendererDiagnostics.FaultCount != 0)
        {
            throw new InvalidOperationException(
                $"Storm reported {finalRendererDiagnostics.FaultCount} renderer faults after teardown.");
        }
        if (ContextLossSimulated && finalRendererDiagnostics.PostLossFrames == 0)
        {
            throw new InvalidOperationException(
                "No successful Storm frame was observed after context loss.");
        }
        if (StormFrames != 0 && finalRendererDiagnostics.ShutdownCompletions == 0)
        {
            throw new InvalidOperationException(
                "Storm teardown completed without an observed render-pump shutdown completion.");
        }
        return this with
        {
            FinalResources = finalResources,
            PreLossStormFrames = finalRendererDiagnostics.PreLossFrames,
            PostLossStormFrames = finalRendererDiagnostics.PostLossFrames,
            RendererFaultCount = finalRendererDiagnostics.FaultCount,
            RendererShutdownCompletions = finalRendererDiagnostics.ShutdownCompletions,
            ResourcesReleased = true
        };
    }

    internal string ToJson()
    {
        var builder = new StringBuilder(8192);
        builder.AppendLine("{");
        Append(builder, "status", "passed", comma: true);
        Append(builder, "startedAt", StartedAt.ToString("O", CultureInfo.InvariantCulture), comma: true);
        Append(builder, "completedAt", CompletedAt.ToString("O", CultureInfo.InvariantCulture), comma: true);
        Append(builder, "orderedOperations", OrderedOperations, comma: true);
        Append(builder, "mutatingOperations", MutatingOperations, comma: true);
        Append(builder, "propertyOperations", PropertyOperations, comma: true);
        Append(builder, "topologyOperations", TopologyOperations, comma: true);
        Append(builder, "compositionOperations", CompositionOperations, comma: true);
        Append(builder, "readOperations", ReadOperations, comma: true);
        Append(builder, "changeNotifications", ChangeNotifications, comma: true);
        Append(builder, "coalescedEditCount", CoalescedEditCount, comma: true);
        Append(builder, "initialChangeSerial", InitialChangeSerial, comma: true);
        Append(builder, "finalChangeSerial", FinalChangeSerial, comma: true);
        Append(builder, "silkSyncPages", SilkSyncPages, comma: true);
        Append(builder, "silkMeshUpserts", SilkMeshUpserts, comma: true);
        Append(builder, "silkMeshRemovals", SilkMeshRemovals, comma: true);
        Append(builder, "silkSteadyPages", SilkSteadyPages, comma: true);
        Append(builder, "peakSilkMeshes", PeakSilkMeshes, comma: true);
        Append(builder, "finalSilkMeshes", FinalSilkMeshes, comma: true);
        Append(builder, "peakPageCommands", PeakPageCommands, comma: true);
        Append(builder, "stormFrames", StormFrames, comma: true);
        Append(builder, "contextLossSimulated", ContextLossSimulated, comma: true);
        Append(
            builder,
            "silkSessionTeardownSimulated",
            SilkSessionTeardownSimulated,
            comma: true);
        Append(builder, "activeChildRejectionObserved", ActiveChildRejectionObserved, comma: true);
        Append(builder, "managedAllocatedBytes", ManagedAllocatedBytes, comma: true);
        Append(builder, "warmManagedHeapBytes", WarmManagedHeapBytes, comma: true);
        Append(builder, "finalManagedHeapBytes", FinalManagedHeapBytes, comma: true);
        Append(builder, "warmWorkingSetBytes", WarmWorkingSetBytes, comma: true);
        Append(builder, "finalWorkingSetBytes", FinalWorkingSetBytes, comma: true);
        Append(builder, "gen0Collections", Gen0Collections, comma: true);
        Append(builder, "gen1Collections", Gen1Collections, comma: true);
        Append(builder, "gen2Collections", Gen2Collections, comma: true);
        Append(builder, "sourceHash", BuildIdentity.SourceHash, comma: true);
        Append(builder, "executableHash", BuildIdentity.ExecutableHash, comma: true);
        Append(
            builder,
            "executableTimestamp",
            BuildIdentity.ExecutableTimestamp.ToString("O", CultureInfo.InvariantCulture),
            comma: true);
        Append(builder, "buildHash", BuildIdentity.BuildHash, comma: true);
        Append(builder, "dataAbi", SharedStageBuildIdentity.DataAbi, comma: true);
        Append(builder, "stormAbi", SharedStageBuildIdentity.StormAbi, comma: true);
        Append(builder, "silkSessionAbi", SharedStageBuildIdentity.SilkSessionAbi, comma: true);
        Append(builder, "silkPageAbi", SharedStageBuildIdentity.SilkPageAbi, comma: true);
        Append(
            builder,
            "managedRetainedSlopeBytesPerThousandEdits",
            ManagedRetainedSlopeBytesPerThousandEdits,
            comma: true);
        Append(
            builder,
            "workingSetSlopeBytesPerThousandEdits",
            WorkingSetSlopeBytesPerThousandEdits,
            comma: true);
        Append(builder, "propertyInvalidations", PropertyInvalidations, comma: true);
        Append(builder, "topologyInvalidations", TopologyInvalidations, comma: true);
        Append(builder, "compositionInvalidations", CompositionInvalidations, comma: true);
        Append(builder, "fullInvalidations", FullInvalidations, comma: true);
        Append(builder, "preLossStormFrames", PreLossStormFrames, comma: true);
        Append(builder, "postLossStormFrames", PostLossStormFrames, comma: true);
        Append(builder, "rendererFaultCount", RendererFaultCount, comma: true);
        Append(
            builder,
            "rendererShutdownCompletions",
            RendererShutdownCompletions,
            comma: true);
        Append(
            builder,
            "targetedColorUpsertObserved",
            TargetedColorUpsertObserved,
            comma: true);
        Append(builder, "finalDisplayColorTime", FinalDisplayColorTime, comma: true);
        AppendFloatArray(
            builder,
            "expectedFinalDisplayColor",
            ExpectedFinalDisplayColor,
            comma: true);
        AppendFloatArray(
            builder,
            "actualFinalDisplayColor",
            ActualFinalDisplayColor,
            comma: true);
        AppendMeshes(builder, "expectedFinalMeshes", ExpectedFinalMeshes, comma: true);
        AppendMeshes(builder, "actualFinalMeshes", ActualFinalMeshes, comma: true);
        AppendUlongArray(builder, "removedMeshIds", RemovedMeshIds, comma: true);
        AppendUlongArray(builder, "restoredMeshIds", RestoredMeshIds, comma: true);
        AppendStringArray(builder, "removedMeshPaths", RemovedMeshPaths, comma: true);
        AppendStringArray(builder, "restoredMeshPaths", RestoredMeshPaths, comma: true);
        AppendResource(builder, "baselineResources", BaselineResources, comma: true);
        AppendResource(builder, "peakResources", PeakResources, comma: true);
        AppendResource(builder, "finalResources", FinalResources, comma: true);
        builder.AppendLine("  \"memoryCheckpoints\": [");
        for (int index = 0; index < MemoryCheckpoints.Length; index++)
        {
            SharedStageMemoryCheckpoint checkpoint = MemoryCheckpoints[index];
            builder.AppendLine("    {");
            Append(builder, "orderedOperation", checkpoint.OrderedOperation, comma: true, indent: 6);
            Append(builder, "mutatingOperation", checkpoint.MutatingOperation, comma: true, indent: 6);
            Append(
                builder,
                "managedRetainedBytes",
                checkpoint.ManagedRetainedBytes,
                comma: true,
                indent: 6);
            Append(builder, "workingSetBytes", checkpoint.WorkingSetBytes, comma: true, indent: 6);
            Append(builder, "changeSerial", checkpoint.ChangeSerial, comma: true, indent: 6);
            Append(builder, "pageRevision", checkpoint.PageRevision, comma: true, indent: 6);
            Append(builder, "meshCount", checkpoint.MeshCount, comma: true, indent: 6);
            Append(
                builder,
                "nativeStageCores",
                checkpoint.Resources.NativeStageCores,
                comma: true,
                indent: 6);
            Append(
                builder,
                "schedulerChildren",
                checkpoint.Resources.SchedulerChildren,
                comma: true,
                indent: 6);
            Append(
                builder,
                "stormRenderers",
                checkpoint.Resources.NativeStormRenderers,
                comma: true,
                indent: 6);
            Append(
                builder,
                "silkSessions",
                checkpoint.Resources.NativeSilkSessions,
                comma: true,
                indent: 6);
            Append(
                builder,
                "silkPages",
                checkpoint.Resources.NativeSilkPages,
                comma: true,
                indent: 6);
            Append(
                builder,
                "gpuMeshResources",
                checkpoint.Resources.GpuMeshResources,
                comma: false,
                indent: 6);
            builder.Append("    }").AppendLine(
                index + 1 == MemoryCheckpoints.Length ? string.Empty : ",");
        }
        builder.AppendLine("  ],");
        Append(builder, "resourcesReleased", ResourcesReleased, comma: false);
        builder.AppendLine("}");
        return builder.ToString();
    }

    internal static string FailureJson(
        DateTimeOffset startedAt,
        Exception exception)
    {
        var builder = new StringBuilder(512);
        builder.AppendLine("{");
        Append(builder, "status", "failed", comma: true);
        Append(builder, "startedAt", startedAt.ToString("O", CultureInfo.InvariantCulture), comma: true);
        Append(
            builder,
            "completedAt",
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            comma: true);
        Append(builder, "error", exception.ToString(), comma: false);
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void Append(
        StringBuilder builder,
        string name,
        string value,
        bool comma)
    {
        builder.Append("  \"").Append(Escape(name)).Append("\": \"")
            .Append(Escape(value)).Append('"');
        builder.AppendLine(comma ? "," : string.Empty);
    }

    private static void Append<T>(
        StringBuilder builder,
        string name,
        T value,
        bool comma)
        where T : ISpanFormattable
        => Append(builder, name, value, comma, indent: 2);

    private static void Append<T>(
        StringBuilder builder,
        string name,
        T value,
        bool comma,
        int indent)
        where T : ISpanFormattable
    {
        builder.Append(' ', indent).Append('"').Append(Escape(name)).Append("\": ")
            .Append(value.ToString(null, CultureInfo.InvariantCulture));
        builder.AppendLine(comma ? "," : string.Empty);
    }

    private static void Append(
        StringBuilder builder,
        string name,
        bool value,
        bool comma)
    {
        builder.Append("  \"").Append(Escape(name)).Append("\": ")
            .Append(value ? "true" : "false");
        builder.AppendLine(comma ? "," : string.Empty);
    }

    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);

    private static void AppendFloatArray(
        StringBuilder builder,
        string name,
        float[] values,
        bool comma)
    {
        builder.Append("  \"").Append(Escape(name)).Append("\": [");
        for (int index = 0; index < values.Length; index++)
        {
            if (index != 0)
            {
                builder.Append(", ");
            }
            builder.Append(values[index].ToString("R", CultureInfo.InvariantCulture));
        }
        builder.Append(']').AppendLine(comma ? "," : string.Empty);
    }

    private static void AppendMeshes(
        StringBuilder builder,
        string name,
        SharedStageMeshIdentity[] meshes,
        bool comma)
    {
        builder.Append("  \"").Append(Escape(name)).AppendLine("\": [");
        for (int index = 0; index < meshes.Length; index++)
        {
            SharedStageMeshIdentity mesh = meshes[index];
            builder.Append("    { \"id\": \"")
                .Append(mesh.Id.ToString(CultureInfo.InvariantCulture))
                .Append("\", \"path\": \"")
                .Append(Escape(mesh.Path))
                .Append("\" }")
                .AppendLine(index + 1 == meshes.Length ? string.Empty : ",");
        }
        builder.Append("  ]").AppendLine(comma ? "," : string.Empty);
    }

    private static void AppendStringArray(
        StringBuilder builder,
        string name,
        string[] values,
        bool comma)
    {
        builder.Append("  \"").Append(Escape(name)).Append("\": [");
        for (int index = 0; index < values.Length; index++)
        {
            if (index != 0)
            {
                builder.Append(", ");
            }
            builder.Append('"').Append(Escape(values[index])).Append('"');
        }
        builder.Append(']');
        builder.AppendLine(comma ? "," : string.Empty);
    }

    private static void AppendUlongArray(
        StringBuilder builder,
        string name,
        ulong[] values,
        bool comma)
    {
        builder.Append("  \"").Append(Escape(name)).Append("\": [");
        for (int index = 0; index < values.Length; index++)
        {
            if (index != 0)
            {
                builder.Append(", ");
            }
            builder.Append('"')
                .Append(values[index].ToString(CultureInfo.InvariantCulture))
                .Append('"');
        }
        builder.Append(']').AppendLine(comma ? "," : string.Empty);
    }

    private static void AppendResource(
        StringBuilder builder,
        string name,
        SharedStageResourceSnapshot resource,
        bool comma)
    {
        builder.Append("  \"").Append(name).AppendLine("\": {");
        Append(builder, "nativeStageCores", resource.NativeStageCores, comma: true, indent: 4);
        Append(builder, "nativePeakStageCores", resource.NativePeakStageCores, comma: true, indent: 4);
        Append(builder, "managedSchedulers", resource.ManagedSchedulers, comma: true, indent: 4);
        Append(builder, "managedRenderSources", resource.ManagedRenderSources, comma: true, indent: 4);
        Append(builder, "managedRenderLeases", resource.ManagedRenderLeases, comma: true, indent: 4);
        Append(builder, "schedulerChildren", resource.SchedulerChildren, comma: true, indent: 4);
        Append(builder, "managedStormRenderers", resource.ManagedStormRenderers, comma: true, indent: 4);
        Append(builder, "nativeStormRenderers", resource.NativeStormRenderers, comma: true, indent: 4);
        Append(builder, "nativePeakStormRenderers", resource.NativePeakStormRenderers, comma: true, indent: 4);
        Append(builder, "abandonedStormEngines", resource.AbandonedStormEngines, comma: true, indent: 4);
        Append(builder, "managedSilkSessions", resource.ManagedSilkSessions, comma: true, indent: 4);
        Append(builder, "nativeSilkSessions", resource.NativeSilkSessions, comma: true, indent: 4);
        Append(builder, "nativePeakSilkSessions", resource.NativePeakSilkSessions, comma: true, indent: 4);
        Append(builder, "managedSilkPages", resource.ManagedSilkPages, comma: true, indent: 4);
        Append(builder, "nativeSilkPages", resource.NativeSilkPages, comma: true, indent: 4);
        Append(builder, "nativePeakSilkPages", resource.NativePeakSilkPages, comma: true, indent: 4);
        Append(builder, "gpuSceneResources", resource.GpuSceneResources, comma: true, indent: 4);
        Append(builder, "gpuMeshResources", resource.GpuMeshResources, comma: false, indent: 4);
        builder.Append("  }").AppendLine(comma ? "," : string.Empty);
    }

    private static void ValidateReclaimed(
        SharedStageResourceSnapshot baseline,
        SharedStageResourceSnapshot final,
        bool contextLossSimulated)
    {
        if (final.NativeStageCores != baseline.NativeStageCores ||
            final.ManagedSchedulers != baseline.ManagedSchedulers ||
            final.ManagedRenderSources != baseline.ManagedRenderSources ||
            final.ManagedRenderLeases != baseline.ManagedRenderLeases ||
            final.SchedulerChildren != baseline.SchedulerChildren ||
            final.ManagedStormRenderers != baseline.ManagedStormRenderers ||
            final.NativeStormRenderers != baseline.NativeStormRenderers ||
            final.ManagedSilkSessions != baseline.ManagedSilkSessions ||
            final.NativeSilkSessions != baseline.NativeSilkSessions ||
            final.ManagedSilkPages != baseline.ManagedSilkPages ||
            final.NativeSilkPages != baseline.NativeSilkPages ||
            final.GpuSceneResources != baseline.GpuSceneResources ||
            final.GpuMeshResources != baseline.GpuMeshResources)
        {
            throw new InvalidOperationException(
                $"Reclaimable shared-stage resources did not return to baseline. " +
                $"Baseline={baseline}; final={final}.");
        }

        long expectedAbandoned = baseline.AbandonedStormEngines +
            (contextLossSimulated ? 1 : 0);
        if (final.AbandonedStormEngines != expectedAbandoned)
        {
            throw new InvalidOperationException(
                $"Abandoned Storm engines ended at {final.AbandonedStormEngines}; " +
                $"expected {expectedAbandoned}.");
        }
    }
}

internal static class SharedStageSoak
{
    private const int PropertyOperationCount = 5_000;
    private const int TopologyOperationCount = 2_500;
    private const int CompositionOperationCount = 2_500;
    private const int ReadOperationCount = 2_500;
    private const int TotalOperationCount =
        PropertyOperationCount +
        TopologyOperationCount +
        CompositionOperationCount +
        ReadOperationCount;
    private const string PropertiesPath = "/World/SoakProperties";
    private const string MeshPath = "/World/SoakMeshA";
    private const string MeshBPath = "/World/SoakMeshB";
    private const string ReferencePath = "/World/SoakReference";
    private const string PayloadPath = "/World/SoakPayload";
    private const string CompositionPath = "/World/SoakComposition";
    private const string ActivePath = "/World/SoakActive";
    private const string SessionPath = "/World/SoakSession";
    private static readonly int[] TriangleCounts = [3];
    private static readonly int[] TriangleIndices = [0, 1, 2];
    private static readonly int[] ReversedTriangleIndices = [0, 2, 1];

    internal static SharedStageSoakOperation GetOperation(int index, int editCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(editCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, editCount);
        ArgumentOutOfRangeException.ThrowIfNotEqual(
            editCount,
            TotalOperationCount);
        return (index % 5) switch
        {
            0 or 3 => SharedStageSoakOperation.Property,
            1 => SharedStageSoakOperation.Topology,
            2 => SharedStageSoakOperation.Composition,
            _ => SharedStageSoakOperation.Read
        };
    }

    internal static async Task<SharedStageSoakResult> RunAsync(
        string pluginPath,
        UsdStageScheduler scheduler,
        UsdStageRenderSource source,
        SharedStageSoakOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginPath);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNotEqual(
            options.EditCount,
            TotalOperationCount,
            nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AssetPath);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.Timeout, TimeSpan.Zero);

        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);
        CancellationToken token = timeout.Token;
        PrepareExternalAsset(options.AssetPath);
        SharedStageNativeDiagnostics.ResetStageCorePeak();
        OpenUsdSilkRuntime.ResetDiagnosticPeaks();
        SharedStageResourceSnapshot peakResources = CaptureResources(
            scheduler,
            options.GetRendererDiagnostics);
        var memoryCheckpoints = new List<SharedStageMemoryCheckpoint>();
        UsdStageSchedulerDiagnosticSnapshot initialInvalidations =
            scheduler.GetDiagnosticSnapshot();

        long allocationStart = GC.GetTotalAllocatedBytes(precise: true);
        int gen0Start = GC.CollectionCount(0);
        int gen1Start = GC.CollectionCount(1);
        int gen2Start = GC.CollectionCount(2);
        var changes = new ChangeMonitor(scheduler, token);
        Task changeTask = changes.RunAsync();

        ulong initialSerial = await scheduler.InvokeAsync(
            stage => stage.ChangeSerial,
            token).ConfigureAwait(false);
        ulong lastSerial = initialSerial;
        int mutations = 0;

        (lastSerial, bool changed) = await ObserveAsync(
            scheduler,
            lastSerial,
            UsdStageInvalidationKind.Topology,
            InitializeStage,
            token).ConfigureAwait(false);
        mutations += changed ? 1 : 0;

        bool activeChildRejected = false;
        try
        {
            await scheduler.DisposeAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            activeChildRejected = true;
        }
        if (!activeChildRejected)
        {
            throw new InvalidOperationException(
                "The scheduler accepted disposal while render children were active.");
        }

        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(pluginPath, source);
        using ISilkGraphicsDevice? graphicsDevice = options.CreateGraphicsDevice?.Invoke();
        using SilkSceneGpuResources? gpuResources = graphicsDevice is null
            ? null
            : new SilkSceneGpuResources(graphicsDevice);
        var sync = new SilkSyncMonitor(session, gpuResources);
        SilkSyncSnapshot initialSync = sync.SyncOnce();
        if (initialSync.LastMeshUpserts < 2 || initialSync.Meshes < 2)
        {
            throw new InvalidOperationException(
                "The initial hdSilk synchronization did not produce mesh data.");
        }
        await sync.WaitForSteadyAsync(token).ConfigureAwait(false);
        SharedStageMeshIdentity[] expectedFinalMeshes = sync.GetMeshIdentities();
        if (!expectedFinalMeshes.Any(mesh => mesh.Path == MeshPath) ||
            !expectedFinalMeshes.Any(mesh => mesh.Path == MeshBPath))
        {
            throw new InvalidOperationException(
                "The initialized steady hdSilk scene did not contain both soak meshes.");
        }
        SilkMeshData primaryBefore = sync.GetMesh(MeshPath);
        SilkMeshData secondaryBefore = sync.GetMesh(MeshBPath);
        (lastSerial, changed) = await ObserveAsync(
            scheduler,
            lastSerial,
            UsdStageInvalidationKind.Property,
            stage => SharedStageNativeDiagnostics.SetDisplayColor(
                stage,
                MeshPath,
                0.125f,
                0.375f,
                0.625f),
            token).ConfigureAwait(false);
        if (!changed)
        {
            throw new InvalidOperationException(
                "The controlled displayColor edit did not advance the change serial.");
        }
        mutations++;
        SilkSyncSnapshot targetedSync = await sync.WaitForMeshChangeAsync(token)
            .ConfigureAwait(false);
        SilkMeshData primaryAfter = sync.GetMesh(MeshPath);
        SilkMeshData secondaryAfter = sync.GetMesh(MeshBPath);
        bool targetedColorUpsertObserved = IsTargetedUpsert(
            primaryBefore.Id,
            targetedSync.LastUpsertedMeshIds,
            targetedSync.LastRemovedMeshIds,
            !ReferenceEquals(primaryBefore, primaryAfter),
            ReferenceEquals(secondaryBefore, secondaryAfter),
            primaryAfter.DisplayColor.Span[0],
            0.125f);
        if (!targetedColorUpsertObserved)
        {
            throw new InvalidOperationException(
                "A property-only displayColor edit did not exclusively upsert the target mesh: " +
                $"target={primaryBefore.Id}, upserts=" +
                $"[{string.Join(',', targetedSync.LastUpsertedMeshIds)}], removals=" +
                $"[{string.Join(',', targetedSync.LastRemovedMeshIds)}], " +
                $"targetReplaced={!ReferenceEquals(primaryBefore, primaryAfter)}, " +
                $"unaffectedStable={ReferenceEquals(secondaryBefore, secondaryAfter)}, " +
                $"color={primaryAfter.DisplayColor.Span[0]}.");
        }
        await sync.WaitForSteadyAsync(token).ConfigureAwait(false);

        using var syncCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task syncTask = sync.RunAsync(syncCancellation.Token);
        long initialStormFrames = options.GetStormFrameCount?.Invoke() ?? 0;
        int propertyOperations = 0;
        int topologyOperations = 0;
        int compositionOperations = 0;
        int readOperations = 0;
        bool contextLossSimulated = false;
        bool silkSessionTeardownSimulated = false;
        var temporarySessionReady = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTemporarySession = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task temporarySessionTask = Task.CompletedTask;
        MemorySnapshot warmMemory = default;

        try
        {
            for (int index = 0; index < options.EditCount; index++)
            {
                token.ThrowIfCancellationRequested();
                SharedStageSoakOperation operation = GetOperation(index, options.EditCount);
                int localIndex = GetLocalIndex(index, options.EditCount, operation);
                switch (operation)
                {
                    case SharedStageSoakOperation.Property:
                        (lastSerial, changed) = await ObserveAsync(
                            scheduler,
                            lastSerial,
                            UsdStageInvalidationKind.Property,
                            stage => EditProperty(stage, localIndex),
                            token).ConfigureAwait(false);
                        propertyOperations++;
                        break;
                    case SharedStageSoakOperation.Topology:
                        (lastSerial, changed) = await ObserveAsync(
                            scheduler,
                            lastSerial,
                            UsdStageInvalidationKind.Topology,
                            stage => EditTopology(stage, localIndex),
                            token).ConfigureAwait(false);
                        topologyOperations++;
                        break;
                    case SharedStageSoakOperation.Composition:
                        (lastSerial, changed) = await ObserveAsync(
                            scheduler,
                            lastSerial,
                            UsdStageInvalidationKind.Composition,
                            stage => EditComposition(stage, localIndex, options.AssetPath),
                            token).ConfigureAwait(false);
                        compositionOperations++;
                        break;
                    case SharedStageSoakOperation.Read:
                        (lastSerial, changed) = await ObserveAsync(
                            scheduler,
                            lastSerial,
                            UsdStageInvalidationKind.Property,
                            stage => ValidateRead(stage, localIndex),
                            token).ConfigureAwait(false);
                        readOperations++;
                        if (changed)
                        {
                            throw new InvalidOperationException(
                                "A no-op validation read advanced the stage change serial.");
                        }
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown soak operation {operation}.");
                }

                if (operation != SharedStageSoakOperation.Read)
                {
                    if (!changed)
                    {
                        throw new InvalidOperationException(
                            $"{operation} operation {localIndex} did not advance the change serial.");
                    }
                    mutations++;
                }
                if (operation == SharedStageSoakOperation.Topology)
                {
                    await sync.WaitForLifecycleObservationAsync(
                        localIndex,
                        MeshPath,
                        MeshBPath,
                        token).ConfigureAwait(false);
                }

                if ((index & 15) == 0)
                {
                    options.RequestStormFrame?.Invoke();
                }

                if (index == 2_499)
                {
                    temporarySessionTask = RunTemporarySessionAsync(
                        pluginPath,
                        source,
                        temporarySessionReady,
                        releaseTemporarySession,
                        token);
                    Task readiness = await Task.WhenAny(
                        temporarySessionReady.Task,
                        temporarySessionTask).ConfigureAwait(false);
                    await readiness.WaitAsync(token).ConfigureAwait(false);
                    await temporarySessionReady.Task.WaitAsync(token)
                        .ConfigureAwait(false);
                }

                if (!silkSessionTeardownSimulated && index == 3_499)
                {
                    silkSessionTeardownSimulated = true;
                    releaseTemporarySession.TrySetResult();
                }

                if (!contextLossSimulated && index == 6_249 &&
                    options.SimulateContextLossAsync is not null)
                {
                    await temporarySessionTask.ConfigureAwait(false);
                    await options.SimulateContextLossAsync(token).ConfigureAwait(false);
                    contextLossSimulated = true;
                }

                if (index >= 2_499 && (index + 1) % 250 == 0)
                {
                    MemorySnapshot memory = CaptureMemory();
                    warmMemory = warmMemory == default ? memory : warmMemory;
                    SilkSyncSnapshot checkpointSync = sync.Snapshot();
                    SharedStageResourceSnapshot resources = CaptureResources(
                        scheduler,
                        options.GetRendererDiagnostics);
                    peakResources = peakResources.Max(resources);
                    memoryCheckpoints.Add(new SharedStageMemoryCheckpoint(
                        index + 1,
                        mutations,
                        memory.ManagedHeapBytes,
                        memory.WorkingSetBytes,
                        lastSerial,
                        checkpointSync.Revision,
                        checkpointSync.Meshes,
                        resources));
                    ValidateRendererFaults(options.GetRendererDiagnostics);
                }
            }

            await sync.WaitForSteadyAsync(token).ConfigureAwait(false);
            await changes.WaitForEditCountAsync(mutations, token).ConfigureAwait(false);
            ValidateOperationCounts(
                options.EditCount,
                propertyOperations,
                topologyOperations,
                compositionOperations,
                readOperations);

            SilkSyncSnapshot finalSync = sync.Snapshot();
            if (finalSync.MeshUpserts <= initialSync.MeshUpserts ||
                finalSync.MeshRemovals == 0)
            {
                throw new InvalidOperationException(
                    "Topology/composition edits did not produce both hdSilk upserts and removals.");
            }
            if (finalSync.Meshes is <= 0 or > 8 || finalSync.PeakMeshes > 8)
            {
                throw new InvalidOperationException(
                    $"hdSilk retained an unexpected mesh count: final={finalSync.Meshes}, " +
                    $"peak={finalSync.PeakMeshes}.");
            }
            if (finalSync.PeakPageCommands > 64)
            {
                throw new InvalidOperationException(
                    $"An hdSilk page grew beyond the bounded scene workload: " +
                    $"{finalSync.PeakPageCommands} commands.");
            }

            await SustainMinimumDurationAsync(
                stopwatch,
                options,
                token).ConfigureAwait(false);
        }
        finally
        {
            releaseTemporarySession.TrySetResult();
            await temporarySessionTask.ConfigureAwait(false);
            syncCancellation.Cancel();
            try
            {
                await syncTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (syncCancellation.IsCancellationRequested)
            {
            }
        }

        ValidateFinalValues(
            await scheduler.InvokeAsync(
                stage =>
                {
                    ValidateFinalValues(stage, int.MaxValue);
                    return true;
                },
                token).ConfigureAwait(false));

        long stormFrames = (options.GetStormFrameCount?.Invoke() ?? 0) - initialStormFrames;
        if (options.GetStormFrameCount is not null && stormFrames < 10)
        {
            throw new InvalidOperationException(
                $"Storm rendered only {stormFrames} frames during the shared-stage soak.");
        }

        MemorySnapshot finalMemory = CaptureMemory();
        if (warmMemory == default)
        {
            warmMemory = finalMemory;
        }
        double managedSlope = CalculateSlope(
            memoryCheckpoints,
            static checkpoint => checkpoint.ManagedRetainedBytes);
        double workingSetSlope = CalculateSlope(
            memoryCheckpoints,
            static checkpoint => checkpoint.WorkingSetBytes);
        ValidateMemoryGrowth(
            warmMemory,
            finalMemory,
            managedSlope,
            workingSetSlope);

        changes.Stop();
        await changeTask.ConfigureAwait(false);
        ChangeSnapshot changeSnapshot = changes.Snapshot();
        SilkSyncSnapshot silkSnapshot = sync.Snapshot();
        SharedStageMeshIdentity[] actualFinalMeshes = sync.GetMeshIdentities();
        ulong[] removedMeshIds = sync.GetRemovedMeshIds();
        ulong[] restoredMeshIds = sync.GetRestoredMeshIds();
        string[] removedMeshPaths = sync.GetRemovedMeshPaths();
        string[] restoredMeshPaths = sync.GetRestoredMeshPaths();
        ValidateFinalMeshState(
            expectedFinalMeshes,
            actualFinalMeshes,
            removedMeshPaths,
            restoredMeshPaths);
        float[] expectedFinalDisplayColor = GetExpectedFinalDisplayColor();
        float[] actualFinalDisplayColor =
            sync.GetMesh(MeshPath).DisplayColor.ToArray();
        ValidateFinalDisplayColor(
            expectedFinalDisplayColor,
            actualFinalDisplayColor,
            "default");
        UsdStageSchedulerDiagnosticSnapshot finalInvalidations =
            scheduler.GetDiagnosticSnapshot();
        long propertyInvalidations =
            finalInvalidations.PropertyInvalidations -
            initialInvalidations.PropertyInvalidations;
        long topologyInvalidations =
            finalInvalidations.TopologyInvalidations -
            initialInvalidations.TopologyInvalidations;
        long compositionInvalidations =
            finalInvalidations.CompositionInvalidations -
            initialInvalidations.CompositionInvalidations;
        long fullInvalidations =
            finalInvalidations.FullInvalidations -
            initialInvalidations.FullInvalidations;
        if (propertyInvalidations != propertyOperations + 1L ||
            topologyInvalidations != topologyOperations + 1L ||
            compositionInvalidations != compositionOperations ||
            fullInvalidations != 0)
        {
            throw new InvalidOperationException(
                "Scheduler invalidation diagnostics did not match the deterministic edit plan: " +
                $"property={propertyInvalidations}, topology={topologyInvalidations}, " +
                $"composition={compositionInvalidations}, full={fullInvalidations}.");
        }

        SharedStageRendererDiagnostics rendererDiagnostics =
            options.GetRendererDiagnostics?.Invoke() ?? default;
        ValidateRendererFaults(options.GetRendererDiagnostics);
        if (contextLossSimulated && rendererDiagnostics.PostLossFrames == 0)
        {
            throw new InvalidOperationException(
                "The simulated context loss did not produce a successful replacement frame.");
        }
        SharedStageResourceSnapshot inRunFinalResources = CaptureResources(
            scheduler,
            options.GetRendererDiagnostics);
        peakResources = peakResources.Max(inRunFinalResources);
        return new SharedStageSoakResult
        {
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            OrderedOperations = options.EditCount,
            MutatingOperations = mutations,
            PropertyOperations = propertyOperations,
            TopologyOperations = topologyOperations,
            CompositionOperations = compositionOperations,
            ReadOperations = readOperations,
            ChangeNotifications = changeSnapshot.Notifications,
            CoalescedEditCount = changeSnapshot.EditCount,
            InitialChangeSerial = initialSerial,
            FinalChangeSerial = lastSerial,
            SilkSyncPages = silkSnapshot.Pages,
            SilkMeshUpserts = silkSnapshot.MeshUpserts,
            SilkMeshRemovals = silkSnapshot.MeshRemovals,
            SilkSteadyPages = silkSnapshot.SteadyPages,
            PeakSilkMeshes = silkSnapshot.PeakMeshes,
            FinalSilkMeshes = silkSnapshot.Meshes,
            PeakPageCommands = silkSnapshot.PeakPageCommands,
            StormFrames = stormFrames,
            ContextLossSimulated = contextLossSimulated,
            SilkSessionTeardownSimulated = silkSessionTeardownSimulated,
            ActiveChildRejectionObserved = activeChildRejected,
            ManagedAllocatedBytes =
                GC.GetTotalAllocatedBytes(precise: true) - allocationStart,
            WarmManagedHeapBytes = warmMemory.ManagedHeapBytes,
            FinalManagedHeapBytes = finalMemory.ManagedHeapBytes,
            WarmWorkingSetBytes = warmMemory.WorkingSetBytes,
            FinalWorkingSetBytes = finalMemory.WorkingSetBytes,
            Gen0Collections = GC.CollectionCount(0) - gen0Start,
            Gen1Collections = GC.CollectionCount(1) - gen1Start,
            Gen2Collections = GC.CollectionCount(2) - gen2Start,
            BuildIdentity = options.BuildIdentity,
            BaselineResources = options.BaselineResources,
            PeakResources = peakResources,
            FinalResources = inRunFinalResources,
            MemoryCheckpoints = [.. memoryCheckpoints],
            ManagedRetainedSlopeBytesPerThousandEdits = managedSlope,
            WorkingSetSlopeBytesPerThousandEdits = workingSetSlope,
            PropertyInvalidations = propertyInvalidations,
            TopologyInvalidations = topologyInvalidations,
            CompositionInvalidations = compositionInvalidations,
            FullInvalidations = fullInvalidations,
            PreLossStormFrames = rendererDiagnostics.PreLossFrames,
            PostLossStormFrames = rendererDiagnostics.PostLossFrames,
            RendererFaultCount = rendererDiagnostics.FaultCount,
            TargetedColorUpsertObserved = targetedColorUpsertObserved,
            FinalDisplayColorTime = "default",
            ExpectedFinalDisplayColor = expectedFinalDisplayColor,
            ActualFinalDisplayColor = actualFinalDisplayColor,
            ExpectedFinalMeshes = expectedFinalMeshes,
            ActualFinalMeshes = actualFinalMeshes,
            RemovedMeshIds = removedMeshIds,
            RestoredMeshIds = restoredMeshIds,
            RemovedMeshPaths = removedMeshPaths,
            RestoredMeshPaths = restoredMeshPaths
        };
    }

    internal static void WriteArtifact(string path, SharedStageSoakResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(result);
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, result.ToJson());
    }

    internal static SharedStageResourceSnapshot CaptureResources(
        UsdStageScheduler? scheduler,
        Func<SharedStageRendererDiagnostics>? getRendererDiagnostics)
    {
        (long liveStageCores, long peakStageCores) =
            SharedStageNativeDiagnostics.GetStageCoreCounts();
        UsdStageSchedulerDiagnosticSnapshot schedulerSnapshot =
            scheduler?.GetDiagnosticSnapshot() ?? default;
        SharedStageRendererDiagnostics renderer =
            getRendererDiagnostics?.Invoke() ?? default;
        (
            long managedSilkSessions,
            long nativeSilkSessions,
            long nativePeakSilkSessions,
            long managedSilkPages,
            long nativeSilkPages,
            long nativePeakSilkPages,
            long gpuScenes,
            long gpuMeshes) = OpenUsdSilkRuntime.GetDiagnostics();
        return new SharedStageResourceSnapshot(
            liveStageCores,
            peakStageCores,
            SharedStageManagedDiagnostics.LiveSchedulers,
            SharedStageManagedDiagnostics.LiveRenderSources,
            SharedStageManagedDiagnostics.LiveRenderLeases,
            schedulerSnapshot.ActiveChildren,
            renderer.ManagedRenderers,
            renderer.NativeRenderers,
            renderer.PeakNativeRenderers,
            renderer.AbandonedEngines,
            managedSilkSessions,
            nativeSilkSessions,
            nativePeakSilkSessions,
            managedSilkPages,
            nativeSilkPages,
            nativePeakSilkPages,
            gpuScenes,
            gpuMeshes);
    }

    internal static void ResetDiagnosticPeaks()
    {
        SharedStageNativeDiagnostics.ResetStageCorePeak();
        OpenUsdSilkRuntime.ResetDiagnosticPeaks();
    }

    internal static void WriteFailureArtifact(
        string path,
        DateTimeOffset startedAt,
        Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(exception);
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(
            fullPath,
            SharedStageSoakResult.FailureJson(startedAt, exception));
    }

    private static void PrepareExternalAsset(string path)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.Delete(fullPath);
        using UsdStage asset = UsdStage.Create(fullPath);
        UsdGeomMesh mesh = asset.DefineMesh("/Asset");
        mesh.SetPoints(CreateTriangle(0.25f));
        mesh.SetTopology(TriangleCounts, TriangleIndices);
        asset.SetDefaultPrim("/Asset");
        asset.Save();
    }

    private static void InitializeStage(UsdStage stage)
    {
        stage.SetEditTargetToRootLayer();
        foreach (string path in
            new[]
            {
                PropertiesPath,
                MeshPath,
                MeshBPath,
                ReferencePath,
                PayloadPath,
                CompositionPath,
                ActivePath,
                SessionPath
            })
        {
            if (stage.HasPrim(path))
            {
                stage.RemovePrim(path);
            }
        }
        if (!stage.HasPrim("/World"))
        {
            stage.DefineXform("/World");
        }
        stage.DefineXform(PropertiesPath);
        UsdGeomMesh mesh = stage.DefineMesh(MeshPath);
        mesh.SetPoints(CreateTriangle(-1));
        mesh.SetTopology(TriangleCounts, TriangleIndices);
        UsdGeomMesh meshB = stage.DefineMesh(MeshBPath);
        meshB.SetPoints(CreateTriangle(1));
        meshB.SetTopology(TriangleCounts, ReversedTriangleIndices);
        stage.OverridePrim(ReferencePath);
        stage.OverridePrim(PayloadPath);
        UsdPrim composition = stage.DefinePrim(CompositionPath);
        composition.AddVariantSet("soakVariant");
        composition.AddVariant("soakVariant", "A");
        composition.AddVariant("soakVariant", "B");
        composition.SetVariantSelection("soakVariant", "B");
        stage.DefineXform(ActivePath);
        stage.DefineXform(SessionPath);
    }

    private static void EditProperty(UsdStage stage, int index)
    {
        UsdPrim prim = stage.GetPrim(PropertiesPath);
        switch (index % 3)
        {
            case 0:
                prim.SetDouble("soak:scalar", index);
                break;
            case 1:
                string colorMeshPath = stage.HasPrim(MeshPath) ? MeshPath : MeshBPath;
                try
                {
                    SharedStageNativeDiagnostics.SetDisplayColor(
                        stage,
                        colorMeshPath,
                        (index % 251) / 250f,
                        ((index * 3) % 251) / 250f,
                        ((index * 7) % 251) / 250f);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"DisplayColor edit {index} failed for {colorMeshPath}.",
                        exception);
                }
                break;
            default:
                prim.SetDouble("soak:timeSample", index, index);
                break;
        }
    }

    private static void EditTopology(UsdStage stage, int index)
    {
        int lifecycle = index % 250;
        if (lifecycle == 245)
        {
            stage.RemovePrim(MeshPath);
            return;
        }
        if (lifecycle == 246)
        {
            UsdGeomMesh recreated = stage.DefineMesh(MeshPath);
            recreated.SetPoints(CreateTriangle(index / 1000f));
            recreated.SetTopology(TriangleCounts, TriangleIndices);
            return;
        }
        if (lifecycle == 247)
        {
            stage.RemovePrim(MeshBPath);
            return;
        }
        if (lifecycle == 248)
        {
            UsdGeomMesh recreated = stage.DefineMesh(MeshBPath);
            recreated.SetPoints(CreateTriangle(index / 1000f));
            recreated.SetTopology(TriangleCounts, ReversedTriangleIndices);
            return;
        }

        string meshPath = (index & 1) == 0 ? MeshPath : MeshBPath;
        UsdGeomMesh mesh = UsdGeomMesh.Wrap(stage.GetPrim(meshPath));
        mesh.SetPoints(CreateTriangle(index / 1000f));
        if ((index & 1) != 0)
        {
            mesh.SetTopology(
                TriangleCounts,
                (index & 2) == 0 ? ReversedTriangleIndices : TriangleIndices);
        }
    }

    private static void EditComposition(
        UsdStage stage,
        int index,
        string assetPath)
    {
        int occurrence = index / 5;
        switch (index % 5)
        {
            case 0:
                stage.SetEditTargetToSessionLayer();
                stage.GetPrim(SessionPath).SetDouble("soak:session", index);
                stage.SetEditTargetToRootLayer();
                break;
            case 1:
                UsdPrim reference = stage.GetPrim(ReferencePath);
                if ((occurrence & 1) == 0)
                {
                    reference.AddReference(assetPath, "/Asset");
                }
                else
                {
                    reference.ClearReferences();
                }
                break;
            case 2:
                UsdPrim payload = stage.GetPrim(PayloadPath);
                if ((occurrence & 1) == 0)
                {
                    payload.AddPayload(assetPath, "/Asset");
                }
                else
                {
                    payload.ClearPayloads();
                }
                break;
            case 3:
                stage.GetPrim(CompositionPath).SetVariantSelection(
                    "soakVariant",
                    (occurrence & 1) == 0 ? "A" : "B");
                break;
            default:
                stage.GetPrim(ActivePath).SetActive((occurrence & 1) != 0);
                break;
        }
    }

    private static void ValidateFinalValues(UsdStage stage, int index)
    {
        UsdPrim properties = stage.GetPrim(PropertiesPath);
        switch (index & 3)
        {
            case 0:
                RequireEqual(properties.GetDouble("soak:scalar"), 4998, "scalar");
                break;
            case 1:
                UsdGeomMesh primaryShape = UsdGeomMesh.Wrap(stage.GetPrim(MeshPath));
                if (primaryShape.GetPoints().Length != 3)
                {
                    throw new InvalidOperationException(
                        "The final primary soak mesh does not contain three points.");
                }
                break;
            case 2:
                RequireEqual(
                    properties.GetDouble("soak:timeSample", 4997),
                    4997,
                    "time sample");
                break;
            default:
                UsdGeomMesh mesh = UsdGeomMesh.Wrap(stage.GetPrim(MeshPath));
                UsdVec3f[] points = mesh.GetPoints();
                if (points.Length != 3)
                {
                    throw new InvalidOperationException(
                        $"The final soak mesh has {points.Length} points instead of 3.");
                }
                RequireEqual(points[0].X, 2.496f, "mesh point");
                UsdGeomMesh meshB = UsdGeomMesh.Wrap(stage.GetPrim(MeshBPath));
                RequireEqual(meshB.GetPoints()[0].X, 2.499f, "secondary mesh point");
                if (!mesh.Prim.IsActive() || !stage.GetPrim(ActivePath).IsActive())
                {
                    throw new InvalidOperationException(
                        "The final soak mesh or composition prim is inactive.");
                }
                if (!string.Equals(
                    stage.GetPrim(CompositionPath).GetVariantSelection("soakVariant"),
                    "B",
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The final variant selection is not B.");
                }
                if (!string.IsNullOrEmpty(stage.GetPrim(ReferencePath).TypeName) ||
                    !string.IsNullOrEmpty(stage.GetPrim(PayloadPath).TypeName))
                {
                    throw new InvalidOperationException(
                        "Reference or payload cleanup did not restore untyped overrides.");
                }
                RequireEqual(
                    stage.GetPrim(SessionPath).GetDouble("soak:session"),
                    2495,
                    "session-layer value");
                break;
        }
    }

    private static void ValidateFinalValues(bool validated)
    {
        if (!validated)
        {
            throw new InvalidOperationException("Final stage validation did not complete.");
        }
    }

    private static void ValidateRead(UsdStage stage, int index)
    {
        if (!stage.HasPrim("/World") ||
            !stage.HasPrim(PropertiesPath) ||
            !stage.HasPrim(CompositionPath))
        {
            throw new InvalidOperationException(
                $"Read {index} could not inspect the deterministic soak prims.");
        }
        foreach (string meshPath in new[] { MeshPath, MeshBPath })
        {
            if (stage.HasPrim(meshPath) &&
                UsdGeomMesh.Wrap(stage.GetPrim(meshPath)).GetPoints().Length != 3)
            {
                throw new InvalidOperationException(
                    $"Read {index} observed invalid topology at {meshPath}.");
            }
        }
    }

    private static async Task<(ulong Serial, bool Changed)> ObserveAsync(
        UsdStageScheduler scheduler,
        ulong previousSerial,
        UsdStageInvalidationKind invalidation,
        Action<UsdStage> action,
        CancellationToken cancellationToken)
    {
        (ulong Before, ulong After) observation = await scheduler.EditAsync(
            stage =>
            {
                ulong before = stage.ChangeSerial;
                action(stage);
                return (before, stage.ChangeSerial);
            },
            invalidation,
            cancellationToken).ConfigureAwait(false);
        if (observation.Before < previousSerial ||
            observation.After < observation.Before)
        {
            throw new InvalidOperationException(
                $"Change serial regressed from {previousSerial} through " +
                $"{observation.Before} to {observation.After}.");
        }
        return (observation.After, observation.After != observation.Before);
    }

    private static int GetLocalIndex(
        int index,
        int editCount,
        SharedStageSoakOperation operation)
    {
        _ = editCount;
        int cycle = index / 5;
        return operation switch
        {
            SharedStageSoakOperation.Property =>
                (cycle * 2) + (index % 5 == 3 ? 1 : 0),
            SharedStageSoakOperation.Topology => cycle,
            SharedStageSoakOperation.Composition => cycle,
            SharedStageSoakOperation.Read => cycle,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    private static UsdVec3f[] CreateTriangle(float offset) =>
    [
        new UsdVec3f(offset, 0, 0),
        new UsdVec3f(offset + 1, 0, 0),
        new UsdVec3f(offset, 1, 0)
    ];

    private static void RequireEqual(double actual, double expected, string name)
    {
        if (Math.Abs(actual - expected) > 0.000001)
        {
            throw new InvalidOperationException(
                $"The final {name} is {actual} instead of {expected}.");
        }
    }

    private static void ValidateOperationCounts(
        int expected,
        int properties,
        int topology,
        int composition,
        int reads)
    {
        if (properties + topology + composition + reads != expected ||
            properties == 0 ||
            topology == 0 ||
            composition == 0 ||
            reads == 0)
        {
            throw new InvalidOperationException(
                "The deterministic soak did not cover every operation category.");
        }
    }

    private static async Task SustainMinimumDurationAsync(
        Stopwatch stopwatch,
        SharedStageSoakOptions options,
        CancellationToken cancellationToken)
    {
        int reportedSecond = -1;
        while (stopwatch.Elapsed < options.MinimumDuration)
        {
            options.RequestStormFrame?.Invoke();
            int elapsedSecond = (int)stopwatch.Elapsed.TotalSeconds;
            if (elapsedSecond != reportedSecond)
            {
                reportedSecond = elapsedSecond;
                options.ReportStatus?.Invoke(
                    $"Shared-stage soak surviving: " +
                    $"{elapsedSecond}/{options.MinimumDuration.TotalSeconds:F0}s");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(16), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static Task RunTemporarySessionAsync(
        string pluginPath,
        UsdStageRenderSource source,
        TaskCompletionSource ready,
        TaskCompletionSource release,
        CancellationToken cancellationToken) =>
        Task.Run(
            async () =>
            {
                using OpenUsdSilkSession temporary =
                    OpenUsdSilkRuntime.Create(pluginPath, source);
                using OpenUsdSilkPage page = temporary.Sync(
                    320,
                    180,
                    camera: CameraState.Default);
                if (page.CommandCount == 0)
                {
                    throw new InvalidOperationException(
                        "The temporary hdSilk session returned an empty initial page.");
                }
                ready.TrySetResult();
                await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    private static MemorySnapshot CaptureMemory()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        return new MemorySnapshot(
            GC.GetTotalMemory(forceFullCollection: false),
            process.WorkingSet64);
    }

    private static void ValidateMemoryGrowth(
        MemorySnapshot warm,
        MemorySnapshot final,
        double managedSlope,
        double workingSetSlope)
    {
        const long managedCeiling = 16L * 1024 * 1024;
        const long workingSetCeiling = 128L * 1024 * 1024;
        const double managedSlopeCeiling = 128 * 1024;
        const double workingSetSlopeCeiling = 4 * 1024 * 1024;
        if (final.ManagedHeapBytes > warm.ManagedHeapBytes + managedCeiling)
        {
            throw new InvalidOperationException(
                $"Managed live memory grew from {warm.ManagedHeapBytes} to " +
                $"{final.ManagedHeapBytes} bytes.");
        }
        if (final.WorkingSetBytes > warm.WorkingSetBytes + workingSetCeiling)
        {
            throw new InvalidOperationException(
                $"Process working set grew from {warm.WorkingSetBytes} to " +
                $"{final.WorkingSetBytes} bytes.");
        }
        if (managedSlope > managedSlopeCeiling)
        {
            throw new InvalidOperationException(
                $"Managed retained memory grew at {managedSlope:F0} bytes per 1,000 edits.");
        }
        if (workingSetSlope > workingSetSlopeCeiling)
        {
            throw new InvalidOperationException(
                $"Working set grew at {workingSetSlope:F0} bytes per 1,000 edits.");
        }
    }

    internal static double CalculateSlope(
        IReadOnlyList<SharedStageMemoryCheckpoint> checkpoints,
        Func<SharedStageMemoryCheckpoint, long> selector)
    {
        ArgumentNullException.ThrowIfNull(checkpoints);
        ArgumentNullException.ThrowIfNull(selector);
        int start = checkpoints.Count / 2;
        int count = checkpoints.Count - start;
        if (count < 3)
        {
            throw new ArgumentException(
                "At least six checkpoints are required for a later-window slope.",
                nameof(checkpoints));
        }

        int slopeCount = (count * (count - 1)) / 2;
        var slopes = new List<double>(slopeCount);
        for (int left = start; left < checkpoints.Count - 1; left++)
        {
            SharedStageMemoryCheckpoint leftCheckpoint = checkpoints[left];
            double leftX = leftCheckpoint.MutatingOperation;
            double leftY = selector(leftCheckpoint);
            for (int right = left + 1; right < checkpoints.Count; right++)
            {
                SharedStageMemoryCheckpoint rightCheckpoint = checkpoints[right];
                double deltaX = rightCheckpoint.MutatingOperation - leftX;
                if (deltaX == 0)
                {
                    continue;
                }

                slopes.Add(((selector(rightCheckpoint) - leftY) / deltaX) * 1000);
            }
        }

        if (slopes.Count == 0)
        {
            return 0;
        }

        slopes.Sort();
        int middle = slopes.Count / 2;
        return (slopes.Count & 1) == 0
            ? (slopes[middle - 1] + slopes[middle]) / 2
            : slopes[middle];
    }

    internal static bool IsTargetedUpsert(
        ulong targetId,
        IReadOnlyList<ulong> upsertedIds,
        IReadOnlyList<ulong> removedIds,
        bool targetReplaced,
        bool unaffectedStable,
        float actualColor,
        float expectedColor) =>
        upsertedIds.Count == 1 &&
        upsertedIds[0] == targetId &&
        removedIds.Count == 0 &&
        targetReplaced &&
        unaffectedStable &&
        Math.Abs(actualColor - expectedColor) <= 0.000001f;

    internal static float[] GetExpectedFinalDisplayColor()
    {
        const int finalPropertyIndex = PropertyOperationCount - 1;
        return
        [
            (finalPropertyIndex % 251) / 250f,
            ((finalPropertyIndex * 3) % 251) / 250f,
            ((finalPropertyIndex * 7) % 251) / 250f,
            1f
        ];
    }

    internal static void ValidateFinalDisplayColor(
        IReadOnlyList<float> expected,
        IReadOnlyList<float> actual,
        string time)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        if (!string.Equals(time, "default", StringComparison.Ordinal) ||
            expected.Count != 4 ||
            actual.Count != 4 ||
            !expected.Zip(actual).All(pair => Math.Abs(pair.First - pair.Second) <= 0.000001f))
        {
            throw new InvalidOperationException(
                "The final authored default-time displayColor did not match the deterministic value. " +
                $"Expected=[{string.Join(',', expected)}], actual=[{string.Join(',', actual)}], " +
                $"time={time}.");
        }
    }

    internal static void ValidateFinalMeshState(
        IReadOnlyList<SharedStageMeshIdentity> expected,
        IReadOnlyList<SharedStageMeshIdentity> actual,
        IReadOnlyCollection<string> removedMeshPaths,
        IReadOnlyCollection<string> restoredMeshPaths)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(removedMeshPaths);
        ArgumentNullException.ThrowIfNull(restoredMeshPaths);
        // Compared by path rather than by (path, ID). Hydra does not reuse a prim ID for
        // a prim re-created at the same path, so requiring the final IDs to equal the
        // initial ones would assert something the renderer never promised; the invariant
        // that matters is that the soak ends with exactly the prims it started with.
        string[] expectedPaths = [.. expected
            .Select(mesh => mesh.Path)
            .OrderBy(path => path, StringComparer.Ordinal)];
        string[] actualPaths = [.. actual
            .Select(mesh => mesh.Path)
            .OrderBy(path => path, StringComparer.Ordinal)];
        if (!expectedPaths.SequenceEqual(actualPaths, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "The final hdSilk mesh path set did not match the initialized steady set. " +
                $"Expected=[{FormatMeshes([.. expected])}], " +
                $"actual=[{FormatMeshes([.. actual])}].");
        }

        foreach (string path in new[] { MeshPath, MeshBPath })
        {
            if (!removedMeshPaths.Contains(path) || !restoredMeshPaths.Contains(path))
            {
                throw new InvalidOperationException(
                    $"Mesh {path} was not observed through both removal and restoration. " +
                    $"Removed=[{string.Join(", ", removedMeshPaths.Order())}], " +
                    $"restored=[{string.Join(", ", restoredMeshPaths.Order())}].");
            }
        }
    }

    private static string FormatMeshes(IEnumerable<SharedStageMeshIdentity> meshes) =>
        string.Join(", ", meshes.Select(mesh => $"{mesh.Id}:{mesh.Path}"));

    private static void ValidateRendererFaults(
        Func<SharedStageRendererDiagnostics>? getRendererDiagnostics)
    {
        SharedStageRendererDiagnostics diagnostics =
            getRendererDiagnostics?.Invoke() ?? default;
        if (diagnostics.FaultCount != 0)
        {
            throw new InvalidOperationException(
                $"Storm reported {diagnostics.FaultCount} renderer faults.");
        }
    }

    private readonly record struct MemorySnapshot(
        long ManagedHeapBytes,
        long WorkingSetBytes);

    private readonly record struct SilkSyncSnapshot(
        long Pages,
        long MeshUpserts,
        long MeshRemovals,
        long SteadyPages,
        int Meshes,
        int PeakMeshes,
        uint PeakPageCommands,
        ulong Revision,
        int LastMeshUpserts,
        int LastMeshRemovals,
        ulong[] LastUpsertedMeshIds,
        ulong[] LastRemovedMeshIds);

    private sealed class SilkSyncMonitor
    {
        private readonly object _gate = new();
        private readonly SilkSceneState _scene = new();
        private readonly OpenUsdSilkSession _session;
        private readonly SilkSceneGpuResources? _gpuResources;
        private long _meshRemovals;
        private long _meshUpserts;
        private long _pages;
        private int _peakMeshes;
        private uint _peakPageCommands;
        private long _steadyPages;
        private int _lastMeshRemovals;
        private int _lastMeshUpserts;
        private ulong[] _lastRemovedMeshIds = [];
        private ulong[] _lastUpsertedMeshIds = [];
        private readonly HashSet<ulong> _removedMeshIds = [];
        private readonly HashSet<ulong> _restoredMeshIds = [];
        // Removal and restoration are tracked by prim path, not by prim ID. A prim ID is
        // Hydra's and is not reused when a prim is deleted and re-created at the same
        // path, which docs/rendering.md allows explicitly by permitting a logical
        // old-prim removal plus new-prim upsert. Path is the identity the renderer and
        // the pick table already resolve by, so it is what proves a prim came back.
        private readonly HashSet<string> _removedMeshPaths = new(StringComparer.Ordinal);
        private readonly HashSet<string> _restoredMeshPaths = new(StringComparer.Ordinal);

        internal SilkSyncMonitor(
            OpenUsdSilkSession session,
            SilkSceneGpuResources? gpuResources)
        {
            _session = session;
            _gpuResources = gpuResources;
        }

        internal SilkSyncSnapshot SyncOnce()
        {
            lock (_gate)
            {
                using OpenUsdSilkPage page = _session.Sync(
                    640,
                    360,
                    camera: CameraState.Default);
                // Captured before the delta is applied so a removed ID can still be
                // resolved to the path it belonged to.
                Dictionary<ulong, string> pathsBeforeApply = _scene.Meshes.ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value.Path);
                SilkSceneDelta delta = _scene.Apply(page);
                _gpuResources?.Apply(_scene, delta);
                _pages++;
                _meshUpserts += delta.MeshUpserts;
                _meshRemovals += delta.MeshRemovals;
                _lastMeshUpserts = delta.MeshUpserts;
                _lastMeshRemovals = delta.MeshRemovals;
                _lastUpsertedMeshIds = delta.UpsertedMeshIds.ToArray();
                _lastRemovedMeshIds = delta.RemovedMeshIds.ToArray();
                foreach (ulong id in delta.RemovedMeshIds.Span)
                {
                    _removedMeshIds.Add(id);
                    if (pathsBeforeApply.TryGetValue(id, out string? removedPath))
                    {
                        _removedMeshPaths.Add(removedPath);
                    }
                }
                foreach (ulong id in delta.UpsertedMeshIds.Span)
                {
                    if (_removedMeshIds.Contains(id))
                    {
                        _restoredMeshIds.Add(id);
                    }

                    if (_scene.Meshes.TryGetValue(id, out SilkMeshData? upserted) &&
                        _removedMeshPaths.Contains(upserted.Path))
                    {
                        _restoredMeshPaths.Add(upserted.Path);
                    }
                }
                if (delta.MeshUpserts == 0 && delta.MeshRemovals == 0)
                {
                    _steadyPages++;
                }
                _peakMeshes = Math.Max(_peakMeshes, _scene.Meshes.Count);
                _peakPageCommands = Math.Max(_peakPageCommands, page.CommandCount);
                return SnapshotCore();
            }
        }

        internal async Task RunAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SyncOnce();
                await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        internal async Task WaitForSteadyAsync(CancellationToken cancellationToken)
        {
            int consecutive = 0;
            for (int attempt = 0; attempt < 200; attempt++)
            {
                SilkSyncSnapshot snapshot = SyncOnce();
                consecutive = snapshot.LastMeshUpserts == 0 &&
                    snapshot.LastMeshRemovals == 0
                    ? consecutive + 1
                    : 0;
                if (consecutive >= 2)
                {
                    return;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(2), cancellationToken)
                    .ConfigureAwait(false);
            }
            throw new TimeoutException("hdSilk did not reach a steady command page.");
        }

        internal async Task<SilkSyncSnapshot> WaitForMeshChangeAsync(
            CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                SilkSyncSnapshot snapshot = SyncOnce();
                if (snapshot.LastMeshUpserts != 0 ||
                    snapshot.LastMeshRemovals != 0)
                {
                    return snapshot;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(2), cancellationToken)
                    .ConfigureAwait(false);
            }
            throw new TimeoutException("hdSilk did not report the expected mesh change.");
        }

        internal async Task WaitForLifecycleObservationAsync(
            int topologyIndex,
            string primaryMeshPath,
            string secondaryMeshPath,
            CancellationToken cancellationToken)
        {
            int lifecycle = topologyIndex % 250;
            (string? path, bool restored) = lifecycle switch
            {
                245 => (primaryMeshPath, false),
                246 => (primaryMeshPath, true),
                247 => (secondaryMeshPath, false),
                248 => (secondaryMeshPath, true),
                _ => default
            };
            if (path is null)
            {
                return;
            }

            for (int attempt = 0; attempt < 200; attempt++)
            {
                SyncOnce();
                lock (_gate)
                {
                    HashSet<string> observed =
                        restored ? _restoredMeshPaths : _removedMeshPaths;
                    if (observed.Contains(path))
                    {
                        return;
                    }
                }
                await Task.Delay(TimeSpan.FromMilliseconds(2), cancellationToken)
                    .ConfigureAwait(false);
            }

            string observedRemoved = string.Join(", ", _removedMeshPaths.Order());
            string observedRestored = string.Join(", ", _restoredMeshPaths.Order());
            string currentMeshes = string.Join(
                ", ",
                _scene.Meshes.Values
                    .Select(mesh => $"{mesh.Path}={mesh.Id}")
                    .Order());
            throw new TimeoutException(
                $"hdSilk did not observe {path} " +
                $"{(restored ? "restoration" : "removal")}. " +
                $"Removed: [{observedRemoved}]. Restored: [{observedRestored}]. " +
                $"Current: [{currentMeshes}].");
        }

        internal SilkMeshData GetMesh(string path)
        {
            lock (_gate)
            {
                return _scene.Meshes.Values.SingleOrDefault(
                    mesh => string.Equals(mesh.Path, path, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException(
                        $"hdSilk did not retain the expected mesh {path}.");
            }
        }

        internal SilkSyncSnapshot Snapshot()
        {
            lock (_gate)
            {
                return SnapshotCore();
            }
        }

        internal SharedStageMeshIdentity[] GetMeshIdentities()
        {
            lock (_gate)
            {
                return [.. _scene.Meshes.Values
                    .Select(mesh => new SharedStageMeshIdentity(mesh.Id, mesh.Path))
                    .OrderBy(mesh => mesh.Path, StringComparer.Ordinal)
                    .ThenBy(mesh => mesh.Id)];
            }
        }

        internal ulong[] GetRemovedMeshIds()
        {
            lock (_gate)
            {
                return [.. _removedMeshIds.Order()];
            }
        }

        internal string[] GetRemovedMeshPaths()
        {
            lock (_gate)
            {
                return [.. _removedMeshPaths.Order(StringComparer.Ordinal)];
            }
        }

        internal string[] GetRestoredMeshPaths()
        {
            lock (_gate)
            {
                return [.. _restoredMeshPaths.Order(StringComparer.Ordinal)];
            }
        }

        internal ulong[] GetRestoredMeshIds()
        {
            lock (_gate)
            {
                return [.. _restoredMeshIds.Order()];
            }
        }

        private SilkSyncSnapshot SnapshotCore() => new(
            _pages,
            _meshUpserts,
            _meshRemovals,
            _steadyPages,
            _scene.Meshes.Count,
            _peakMeshes,
            _peakPageCommands,
            _scene.Revision,
            _lastMeshUpserts,
            _lastMeshRemovals,
            [.. _lastUpsertedMeshIds],
            [.. _lastRemovedMeshIds]);
    }

    private readonly record struct ChangeSnapshot(
        int Notifications,
        int EditCount);

    private sealed class ChangeMonitor
    {
        private readonly CancellationTokenSource _stop;
        private readonly UsdStageScheduler _scheduler;
        private readonly object _gate = new();
        private int _editCount;
        private ulong _lastSerial;
        private int _notifications;

        internal ChangeMonitor(
            UsdStageScheduler scheduler,
            CancellationToken cancellationToken)
        {
            _scheduler = scheduler;
            _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        internal async Task RunAsync()
        {
            try
            {
                await foreach (UsdStageChange change in
                    _scheduler.ReadChangesAsync(_stop.Token).ConfigureAwait(false))
                {
                    lock (_gate)
                    {
                        if (_lastSerial != 0 &&
                            change.BeforeChangeSerial < _lastSerial)
                        {
                            throw new InvalidOperationException(
                                "The stage change feed regressed its serial range.");
                        }
                        _lastSerial = change.AfterChangeSerial;
                        _notifications++;
                        _editCount = checked(_editCount + change.EditCount);
                    }
                    await Task.Delay(TimeSpan.FromMilliseconds(2), _stop.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
        }

        internal async Task WaitForEditCountAsync(
            int expected,
            CancellationToken cancellationToken)
        {
            while (Snapshot().EditCount < expected)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(2), cancellationToken)
                    .ConfigureAwait(false);
            }
            ChangeSnapshot snapshot = Snapshot();
            if (snapshot.EditCount != expected)
            {
                throw new InvalidOperationException(
                    $"The change feed represented {snapshot.EditCount} edits instead of {expected}.");
            }
            if (snapshot.Notifications >= snapshot.EditCount)
            {
                throw new InvalidOperationException(
                    "The bounded change feed did not exercise notification coalescing.");
            }
        }

        internal ChangeSnapshot Snapshot()
        {
            lock (_gate)
            {
                return new ChangeSnapshot(_notifications, _editCount);
            }
        }

        internal void Stop() => _stop.Cancel();
    }
}
