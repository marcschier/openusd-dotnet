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

        using var renderer = new SilkMeshRenderer(device);
        return CaptureCore(session, device, renderer, width, height, renderSettings, timeCode, camera);
    }

    /// <summary>
    /// Renders and captures one RGBA8 frame from what a renderer already retains, without
    /// synchronizing hdSilk.
    /// </summary>
    /// <remarks>
    /// A live renderer - a viewer's presentation renderer, say - synchronizes its session on
    /// every presented frame, and <see cref="OpenUsdSilkSession.Sync"/> reports only what changed
    /// since the previous synchronization. Capturing such a session through
    /// <see cref="Capture(OpenUsdSilkSession, ISilkGraphicsDevice, int, int, double, CameraState)"/>
    /// or <see cref="SilkFrameCapturer"/> would synchronize a session with nothing left to report
    /// and render an empty frame, so a capture taken alongside a running render loop has to reuse
    /// that renderer's retained scene instead. The frame is rendered from the camera, time code,
    /// and complexity of the most recent synchronization.
    /// </remarks>
    /// <param name="renderer">The renderer whose retained scene is rendered.</param>
    /// <param name="device">The graphics device that renders and reads back the frame.</param>
    /// <param name="width">The capture width in pixels.</param>
    /// <param name="height">The capture height in pixels.</param>
    /// <param name="renderSettings">The render settings applied to the capture.</param>
    /// <param name="pageRevision">
    /// The hdSilk page revision the renderer last retained, reported as
    /// <see cref="SilkFrameCaptureResult.PageRevision"/>. A retained capture consumes no hdSilk
    /// commands, so <see cref="SilkFrameCaptureResult.CommandCount"/> is always zero.
    /// </param>
    /// <returns>The captured frame.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="renderer"/> or <paramref name="device"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The renderer retains no geometry, so the capture would be blank.
    /// </exception>
    public static SilkFrameCaptureResult CaptureRetained(
        ISilkRenderTargetRenderer renderer,
        ISilkGraphicsDevice device,
        int width,
        int height,
        RenderSettings renderSettings,
        ulong pageRevision = 0)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        using ISilkGraphicsTexture color = CreateColorTarget(device, width, height);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(checked((uint)width), checked((uint)height)));
        SilkMeshRenderResult result = renderer.Render(
            color,
            depth,
            CreateRenderOptions(renderSettings));
        if (result.DrawCount == 0 && renderer is SilkMeshRenderer { Scene.Meshes.Count: 0 })
        {
            throw new InvalidOperationException(
                "The renderer retains no hdSilk geometry to capture, so the capture would be " +
                "blank. Render at least one synchronized frame before capturing, or capture " +
                "through SilkFrameCapture.Capture or SilkFrameCapturer to synchronize a session.");
        }

        return ReadbackFrame(color, width, height, result, pageRevision, commandCount: 0);
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
        using ISilkGraphicsTexture color = CreateColorTarget(device, width, height);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(checked((uint)width), checked((uint)height)));
        using OpenUsdSilkPage page = session.Sync(
            width,
            height,
            timeCode,
            camera,
            renderSettings.Complexity);
        SilkMeshRenderOptions options = CreateRenderOptions(renderSettings);
        SilkMeshRenderResult result = renderer.ApplyAndRender(page, color, depth, options);
        if (result.DrawCount == 0 && renderer.Scene.Meshes.Count == 0)
        {
            throw new InvalidOperationException(
                "The hdSilk session produced no geometry to render. Sync reports only what changed " +
                "since the previous synchronization, so a session that has already been synchronized " +
                "yields an empty page and would capture a blank frame. Use SilkFrameCapturer to " +
                "capture more than once from the same session, or create a session per capture.");
        }

        return ReadbackFrame(color, width, height, result, page.Revision, page.CommandCount);
    }

    private static ISilkGraphicsTexture CreateColorTarget(
        ISilkGraphicsDevice device,
        int width,
        int height) =>
        device.CreateTexture2D(
            new SilkTextureDescriptor(
                checked((uint)width),
                checked((uint)height),
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));

    private static SilkMeshRenderOptions CreateRenderOptions(RenderSettings renderSettings) =>
        new(
            new SilkColor(
                renderSettings.ClearColor.X,
                renderSettings.ClearColor.Y,
                renderSettings.ClearColor.Z,
                renderSettings.ClearColor.W),
            1,
            renderSettings.BackfaceCulling,
            renderSettings.UseSceneMaterials);

    private static SilkFrameCaptureResult ReadbackFrame(
        ISilkGraphicsTexture color,
        int width,
        int height,
        SilkMeshRenderResult result,
        ulong pageRevision,
        uint commandCount)
    {
        byte[] rgba = new byte[checked(width * height * 4)];
        color.ReadbackForTesting(rgba);
        return new SilkFrameCaptureResult(
            width,
            height,
            rgba,
            result,
            pageRevision,
            commandCount);
    }
}
