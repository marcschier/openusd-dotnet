// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

internal sealed class ViewerCompletedJobTestFiles : IDisposable
{
    internal string Root { get; } = Directory.CreateTempSubdirectory("viewer-completed-").FullName;

    internal RenderDiskJobResult CreateJob(
        int count = 3, int width = 2, int height = 1, byte[]? pixels = null,
        Rgba8RowOrder rowOrder = Rgba8RowOrder.TopDown)
    {
        string directory = Path.Combine(Root, $"render-job-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        using var encoded = new MemoryStream();
        _ = PngRgba8Writer.Write(encoded, width, height, pixels ?? [255, 0, 0, 128, 0, 0, 255, 64],
            rowOrder, 64 * 1024 * 1024);
        byte[] png = encoded.ToArray();
        string sha = Convert.ToHexString(SHA256.HashData(png));
        var frames = new RenderDiskFrameResult[count];
        for (int index = 0; index < frames.Length; index++)
        {
            var state = StageRenderState.Create(new StageIdentity("original-completed-stage"))
                .WithViewport(new ViewportDimensions(width, height)).WithTime(new StageTime(index));
            string name = FormattableString.Invariant($"frame-{index:D6}.png");
            File.WriteAllBytes(Path.Combine(directory, name), png);
            frames[index] = new RenderDiskFrameResult(index, state, name, png.Length, sha);
        }
        return new RenderDiskJobResult(directory, frames, png.Length * (long)count, []);
    }

    internal static RenderDiskJobResult ReplaceLastFrame(
        RenderDiskJobResult job, string? name = null, int? index = null, StageRenderState? state = null,
        long? bytes = null, string? sha256 = null)
    {
        RenderDiskFrameResult[] frames = job.Frames.ToArray();
        RenderDiskFrameResult last = frames[^1];
        frames[^1] = new RenderDiskFrameResult(index ?? last.Index, state ?? last.State,
            name ?? last.FileName, bytes ?? last.Bytes, sha256 ?? last.Sha256);
        return new RenderDiskJobResult(job.OutputDirectory, frames, job.TotalBytes, []);
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
