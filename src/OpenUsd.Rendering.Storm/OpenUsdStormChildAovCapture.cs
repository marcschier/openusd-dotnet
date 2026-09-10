// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Storm;

/// <summary>Detached native viewport pixels and typed outputs from one child render-thread operation.</summary>
/// <remarks>
/// The framebuffer preserves native Storm appearance and bottom-up rows. AOV rows are top-down.
/// The operation refuses stage changes between its output captures. It does not remove selection,
/// apply authored product filters, or certify scene-linear color primaries.
/// </remarks>
public sealed class OpenUsdStormChildAovCapture
{
    internal const ulong ManagedCompanionAllowance = 512;
    internal const ulong NativeCompanionAllowance = 16_384;

    internal OpenUsdStormChildAovCapture(
        OpenUsdStormFramebufferCapture framebuffer,
        StormAovSnapshot aovs)
    {
        Framebuffer = framebuffer;
        Aovs = aovs;
        ManagedStorageUpperBound = checked(aovs.ManagedStorageUpperBound +
            (ulong)framebuffer.RgbaPixels.Length + ManagedCompanionAllowance);
        NativeWorkingBytesUpperBound = checked(aovs.NativeWorkingBytesUpperBound +
            (ulong)framebuffer.RgbaPixels.Length + NativeCompanionAllowance);
    }

    /// <summary>Gets the copied native RGBA8 presentation with bottom-up rows.</summary>
    public OpenUsdStormFramebufferCapture Framebuffer { get; }

    /// <summary>Gets immutable typed native outputs and their captured selection qualification.</summary>
    public StormAovSnapshot Aovs { get; }

    /// <summary>Gets admitted snapshot and native-presentation copy storage, not process RSS.</summary>
    public ulong ManagedStorageUpperBound { get; }

    /// <summary>Gets admitted known native AOV, RGBA8 and command storage, not GPU or scene memory.</summary>
    public ulong NativeWorkingBytesUpperBound { get; }

    /// <summary>Copies native presentation and optional raw AOV planes into a bounded disk-job image.</summary>
    /// <param name="includeDeviceDepth">Include actual top-down normalized OpenGL depth.</param>
    /// <param name="includeHdrColor">Include exact raw half color only when capture had no display selection.</param>
    /// <param name="maximumManagedBytes">Existing capture plus new image storage, at most 64 MiB.</param>
    /// <param name="cancellationToken">Cancels before allocation or between bounded pixel batches.</param>
    /// <returns>An independently owned image preserving native PNG appearance and each plane's row order.</returns>
    /// <remarks>No native calls, rerender, exposure, tone-map or OCIO conversion is performed.</remarks>
    public RenderJobImage CreateJobImage(
        bool includeDeviceDepth = false,
        bool includeHdrColor = false,
        long maximumManagedBytes = 64 * 1024 * 1024,
        CancellationToken cancellationToken = default) =>
        Aovs.CreateJobImageWithNativePresentation(Framebuffer.RgbaPixels, includeDeviceDepth,
            includeHdrColor, maximumManagedBytes, cancellationToken);
}
