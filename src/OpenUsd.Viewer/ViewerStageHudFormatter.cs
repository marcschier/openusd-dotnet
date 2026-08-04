// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text;

namespace OpenUsd.Viewer;

internal static class ViewerStageHudFormatter
{
    internal static string FormatIdentity(ViewerStageStatisticsSnapshot statistics) =>
        string.IsNullOrEmpty(statistics.RootLayerIdentifier)
            ? "Identity: —"
            : $"Root: {ViewerScalarFormatter.Bound(statistics.RootLayerIdentifier, 256)}" +
                Environment.NewLine +
                $"Session: {ViewerScalarFormatter.Bound(statistics.SessionLayerIdentifier, 256)}" +
                Environment.NewLine +
                $"Default prim: " +
                $"{(string.IsNullOrEmpty(statistics.DefaultPrimPath) ? "<none>" : statistics.DefaultPrimPath)}";

    internal static string FormatStatistics(
        ViewerStageStatisticsSnapshot statistics,
        ViewerStageTimingSnapshot timing,
        ViewerDiagnosticsSnapshot diagnostics)
    {
        if (statistics.PrimCount == 0)
        {
            return "Statistics: no traversable prims";
        }

        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"Traversable prims: {statistics.PrimCount}; ");
        builder.Append(CultureInfo.InvariantCulture, $"roots: {statistics.RootPrimCount}; ");
        builder.Append(CultureInfo.InvariantCulture, $"leaves: {statistics.LeafPrimCount}; ");
        builder.Append(CultureInfo.InvariantCulture, $"maximum depth: {statistics.MaximumDepth}");
        builder.AppendLine();
        builder.Append(CultureInfo.InvariantCulture, $"Meshes: {statistics.MeshCount}; ");
        builder.Append(CultureInfo.InvariantCulture, $"mesh vertices: {statistics.MeshVertexCount}; ");
        builder.Append(CultureInfo.InvariantCulture, $"curve CVs: {statistics.CurveVertexCount}; ");
        builder.Append(CultureInfo.InvariantCulture, $"faces: {statistics.FaceCount}");
        builder.AppendLine();
        builder.Append("Playback: ");
        builder.Append(CultureInfo.InvariantCulture, $"{timing.StartTimeCode:0.###}..{timing.EndTimeCode:0.###}; ");
        builder.Append(CultureInfo.InvariantCulture, $"FPS {timing.FramesPerSecond:0.###}; ");
        builder.Append(CultureInfo.InvariantCulture, $"TCPS {timing.TimeCodesPerSecond:0.###}");
        builder.AppendLine();
        builder.Append("Render: ");
        if (diagnostics.Timestamp == DateTimeOffset.MinValue)
        {
            builder.Append("no frame sampled");
        }
        else
        {
            builder.Append(CultureInfo.InvariantCulture, $"CPU {FormatDuration(diagnostics.FrameDuration)}; ");
            builder.Append("GPU ");
            builder.Append(diagnostics.GpuDuration is { } gpuDuration
                ? FormatDuration(gpuDuration)
                : "—");
            builder.Append(CultureInfo.InvariantCulture, $"; draws {diagnostics.DrawCalls}; ");
            builder.Append(CultureInfo.InvariantCulture, $"triangles {diagnostics.Triangles}");
        }
        builder.AppendLine();
        builder.Append("AABB: ");
        builder.Append(statistics.WorldBounds.IsEmpty ? "empty" : statistics.WorldBounds.ToString());
        builder.Append(CultureInfo.InvariantCulture, $"; bbox query {FormatDuration(statistics.BoundsQueryDuration)}");
        return builder.ToString();
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalMilliseconds < 1
            ? "<1 ms"
            : duration.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture) + " ms";
}
