// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Managed-owned RGBA8 frame captured from an hdSilk offscreen render target.
/// </summary>
public sealed class SilkFrameCaptureResult
{
    internal SilkFrameCaptureResult(
        int width,
        int height,
        byte[] rgba,
        SilkMeshRenderResult renderResult,
        ulong pageRevision,
        uint commandCount)
    {
        Width = width;
        Height = height;
        Rgba = rgba;
        RenderResult = renderResult;
        PageRevision = pageRevision;
        CommandCount = commandCount;
    }

    /// <summary>Gets the captured width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the captured height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets tightly packed RGBA8 pixels in top-down row order.</summary>
    public ReadOnlyMemory<byte> Rgba { get; }

    /// <summary>Gets renderer evidence for the captured frame.</summary>
    public SilkMeshRenderResult RenderResult { get; }

    /// <summary>Gets the hdSilk page revision rendered into the capture.</summary>
    public ulong PageRevision { get; }

    /// <summary>Gets the number of hdSilk commands consumed by the capture.</summary>
    public uint CommandCount { get; }
}

/// <summary>
/// Captures hdSilk frames through the same sync, render, and readback path used by
/// the conformance harness.
/// </summary>
public static class SilkFrameCapture
{
    /// <summary>Synchronizes, renders, and captures one RGBA8 frame with default render settings.</summary>
    public static SilkFrameCaptureResult Capture(
        OpenUsdSilkSession session,
        ISilkGraphicsDevice device,
        int width,
        int height,
        double timeCode = 0,
        CameraState camera = default) =>
        Capture(session, device, width, height, RenderSettings.Default, timeCode, camera);

    /// <summary>Synchronizes, renders, and captures one RGBA8 frame.</summary>
    /// <remarks>
    /// This is a one-shot helper: it builds a renderer per call, while
    /// <see cref="OpenUsdSilkSession.Sync"/> reports only what changed since the previous
    /// synchronization. A session that has already been synchronized therefore yields a page
    /// with no geometry, which would render an empty frame. Use <see cref="SilkFrameCapturer"/>
    /// to capture repeatedly from one session.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The session has already been synchronized, so no geometry remains to render.
    /// </exception>
    public static SilkFrameCaptureResult Capture(
        OpenUsdSilkSession session,
        ISilkGraphicsDevice device,
        int width,
        int height,
        RenderSettings renderSettings,
        double timeCode = 0,
        CameraState camera = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (session.HasSynchronized)
        {
            throw new InvalidOperationException(
                "The hdSilk session has already been synchronized. Sync reports only what changed " +
                "since the previous synchronization, so the one-shot capture helper would receive " +
                "an empty page and capture a blank frame. Use SilkFrameCapturer to capture more " +
                "than once from the same session, or create a session per capture.");
        }

        using var renderer = new SilkMeshRenderer(device);
        return CaptureCore(session, device, renderer, width, height, renderSettings, timeCode, camera);
    }

    internal static SilkFrameCaptureResult CaptureCore(
        OpenUsdSilkSession session,
        ISilkGraphicsDevice device,
        SilkMeshRenderer renderer,
        int width,
        int height,
        RenderSettings renderSettings,
        double timeCode,
        CameraState camera)
    {
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            new SilkTextureDescriptor(
                checked((uint)width),
                checked((uint)height),
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(checked((uint)width), checked((uint)height)));
        using OpenUsdSilkPage page = session.Sync(
            width,
            height,
            timeCode,
            camera,
            renderSettings.Complexity);
        var options = new SilkMeshRenderOptions(
            new SilkColor(
                renderSettings.ClearColor.X,
                renderSettings.ClearColor.Y,
                renderSettings.ClearColor.Z,
                renderSettings.ClearColor.W),
            1,
            renderSettings.BackfaceCulling,
            renderSettings.UseSceneMaterials);
        SilkMeshRenderResult result = renderer.ApplyAndRender(page, color, depth, options);

        byte[] rgba = new byte[checked(width * height * 4)];
        color.ReadbackForTesting(rgba);
        return new SilkFrameCaptureResult(
            width,
            height,
            rgba,
            result,
            page.Revision,
            page.CommandCount);
    }
}
