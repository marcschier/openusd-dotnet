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
        uint commandCount,
        RenderDiagnosticsState diagnostics)
    {
        Width = width;
        Height = height;
        Rgba = rgba;
        RenderResult = renderResult;
        PageRevision = pageRevision;
        CommandCount = commandCount;
        Diagnostics = diagnostics;
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

    /// <summary>Gets bounded material and texture degradation diagnostics for the frame.</summary>
    public RenderDiagnosticsState Diagnostics { get; }
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

    /// <summary>
    /// Synchronizes, renders, and captures one RGBA8 frame using an OpenColorIO processor
    /// for display-referred output.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The OCIO processor replaces the built-in <see cref="RenderOutputTransform"/> pipeline.
    /// <see cref="RenderSettings.OutputTransform"/> must be
    /// <see cref="RenderOutputTransform.Identity"/>; supplying any other transform alongside
    /// an OCIO processor is rejected to prevent double-transforming.
    /// </para>
    /// <para>
    /// <see cref="RenderSettings.Exposure"/> is applied to linear RGB channels before the
    /// OCIO display/view transform, matching the existing exposure-first ordering.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="renderSettings"/> specifies a non-Identity
    /// <see cref="RenderOutputTransform"/> alongside an OCIO processor.
    /// </exception>
    public static SilkFrameCaptureResult Capture(
        OpenUsdSilkSession session,
        ISilkGraphicsDevice device,
        int width,
        int height,
        RenderSettings renderSettings,
        SilkOpenColorIoProcessor ocioProcessor,
        double timeCode = 0,
        CameraState camera = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(ocioProcessor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ValidateOcioSettings(renderSettings);

        bool wasSynchronized = session.HasSynchronized;
        using var renderer = new SilkMeshRenderer(device);
        SilkFrameCaptureResult result = CaptureCoreOcio(
            session,
            device,
            renderer,
            width,
            height,
            renderSettings,
            ocioProcessor,
            timeCode,
            camera);
        if (wasSynchronized && result.RenderResult.DrawCount == 0 && renderer.Scene.Meshes.Count == 0)
        {
            throw new InvalidOperationException(
                "The hdSilk session was already synchronized and reported no geometry, so this " +
                "capture would return a blank frame. Sync reports only what changed since the " +
                "previous synchronization, and the one-shot helper builds a renderer per call " +
                "with no retained scene. Use SilkFrameCapturer to capture more than once from " +
                "the same session, or create a session per capture.");
        }

        return result;
    }

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

        // A repeat capture is not automatically wrong. Sync reports what changed since the
        // previous synchronization, so a second capture whose stage genuinely changed -- a new
        // time code on a time-sampled attribute, for example -- receives a real page and renders
        // correctly through a fresh renderer. Rejecting every already-synchronized session broke
        // exactly that case.
        //
        // The failure being guarded is narrower: a repeat capture that produced nothing, against
        // a renderer with no retained scene to fall back on. That is the silent blank frame. A
        // first capture of a stage with no renderable geometry is legitimately blank and must
        // still succeed, which is why the session state is read before the sync.
        bool wasSynchronized = session.HasSynchronized;
        using var renderer = new SilkMeshRenderer(device);
        SilkFrameCaptureResult result = CaptureCore(
            session,
            device,
            renderer,
            width,
            height,
            renderSettings,
            timeCode,
            camera);
        if (wasSynchronized && result.RenderResult.DrawCount == 0 && renderer.Scene.Meshes.Count == 0)
        {
            throw new InvalidOperationException(
                "The hdSilk session was already synchronized and reported no geometry, so this " +
                "capture would return a blank frame. Sync reports only what changed since the " +
                "previous synchronization, and the one-shot helper builds a renderer per call " +
                "with no retained scene. Use SilkFrameCapturer to capture more than once from " +
                "the same session, or create a session per capture.");
        }

        return result;
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
    /// or <see cref="SilkFrameCapturer"/> would synchronize a session with nothing left to report,
    /// so a capture taken alongside a running render loop has to reuse that renderer's retained
    /// scene instead. The frame is rendered from the camera, time code, and complexity of the
    /// most recent synchronization.
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
        renderSettings.ValidateDisplayTransform();

        if (renderer is not SilkMeshRenderer silkRenderer)
        {
            if (renderSettings.DisplayTransform is not null)
            {
                throw new NotSupportedException(
                    "A colour-managed display transform is only supported with the " +
                    "built-in SilkMeshRenderer.");
            }
            return CaptureCustomRenderer(
                renderer,
                device,
                width,
                height,
                renderSettings,
                pageRevision);
        }

        using IDisposable captureLease = silkRenderer.AcquireDisplayCaptureLease();
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(
                checked((uint)width),
                checked((uint)height)));
        if (renderSettings.DisplayTransform is not null)
        {
            using ISilkGraphicsTexture display = CreateDisplayTarget(device, width, height);
            SilkMeshRenderResult transformedResult =
                silkRenderer.RenderForDisplayCapture(
                    display,
                    depth,
                    CreateDisplayTransformRenderOptions(renderSettings));
            return ReadbackDisplayTransformedFrame(
                silkRenderer,
                display,
                depth,
                width,
                height,
                transformedResult,
                pageRevision,
                commandCount: 0);
        }

        using ISilkGraphicsTexture color = CreateColorTarget(device, width, height);
        SilkMeshRenderOptions options = CreateRenderOptions(renderSettings);
        SilkMeshRenderResult result =
            silkRenderer.RenderForDisplayCapture(color, depth, options);
        return ReadbackFrame(
            device,
            silkRenderer,
            color,
            depth,
            width,
            height,
            renderSettings,
            result,
            pageRevision,
            commandCount: 0,
            silkRenderer.GpuResources.Diagnostics);
    }

    /// <summary>
    /// Renders and captures one RGBA8 frame from a retained renderer using an OpenColorIO
    /// processor for display-referred output.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="renderSettings"/> specifies a non-Identity
    /// <see cref="RenderOutputTransform"/> alongside an OCIO processor.
    /// </exception>
    public static SilkFrameCaptureResult CaptureRetained(
        ISilkRenderTargetRenderer renderer,
        ISilkGraphicsDevice device,
        int width,
        int height,
        RenderSettings renderSettings,
        SilkOpenColorIoProcessor ocioProcessor,
        ulong pageRevision = 0)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(ocioProcessor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ValidateOcioSettings(renderSettings);

        if (renderer is not SilkMeshRenderer silkRenderer)
        {
            throw new NotSupportedException(
                "OCIO capture is only supported with the built-in SilkMeshRenderer.");
        }

        using IDisposable captureLease = silkRenderer.AcquireDisplayCaptureLease();
        using ISilkGraphicsTexture color = CreateColorTarget(device, width, height);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(
                checked((uint)width),
                checked((uint)height)));
        SilkMeshRenderOptions options = CreateRenderOptions(renderSettings);
        SilkMeshRenderResult result =
            silkRenderer.RenderForDisplayCapture(color, depth, options);
        return ReadbackFrameOcio(
            device,
            silkRenderer,
            color,
            depth,
            width,
            height,
            renderSettings,
            ocioProcessor,
            result,
            pageRevision,
            commandCount: 0,
            silkRenderer.GpuResources.Diagnostics);
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
        renderSettings.ValidateDisplayTransform();
        using IDisposable captureLease = renderer.AcquireDisplayCaptureLease();
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(
                checked((uint)width),
                checked((uint)height)));
        if (renderSettings.DisplayTransform is not null)
        {
            using ISilkGraphicsTexture display = CreateDisplayTarget(device, width, height);
            using OpenUsdSilkPage transformedPage = session.Sync(
                width,
                height,
                timeCode,
                camera,
                renderSettings.Complexity);
            SilkMeshRenderResult transformedResult =
                renderer.ApplyAndRenderForDisplayCapture(
                    transformedPage,
                    display,
                    depth,
                    CreateDisplayTransformRenderOptions(renderSettings));
            return ReadbackDisplayTransformedFrame(
                renderer,
                display,
                depth,
                width,
                height,
                transformedResult,
                transformedPage.Revision,
                transformedPage.CommandCount);
        }

        using ISilkGraphicsTexture color = CreateColorTarget(device, width, height);
        using OpenUsdSilkPage page = session.Sync(
            width,
            height,
            timeCode,
            camera,
            renderSettings.Complexity);
        SilkMeshRenderResult result = renderer.ApplyAndRenderForDisplayCapture(
            page,
            color,
            depth,
            CreateRenderOptions(renderSettings));

        return ReadbackFrame(
            device,
            renderer,
            color,
            depth,
            width,
            height,
            renderSettings,
            result,
            page.Revision,
            page.CommandCount,
            renderer.GpuResources.Diagnostics);
    }

    internal static SilkFrameCaptureResult CaptureCoreOcio(
        OpenUsdSilkSession session,
        ISilkGraphicsDevice device,
        SilkMeshRenderer renderer,
        int width,
        int height,
        RenderSettings renderSettings,
        SilkOpenColorIoProcessor ocioProcessor,
        double timeCode,
        CameraState camera)
    {
        ValidateOcioSettings(renderSettings);
        using IDisposable captureLease = renderer.AcquireDisplayCaptureLease();
        using ISilkGraphicsTexture color = CreateColorTarget(device, width, height);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(
                checked((uint)width),
                checked((uint)height)));
        using OpenUsdSilkPage page = session.Sync(
            width,
            height,
            timeCode,
            camera,
            renderSettings.Complexity);
        SilkMeshRenderResult result = renderer.ApplyAndRenderForDisplayCapture(
            page,
            color,
            depth,
            CreateRenderOptions(renderSettings));

        return ReadbackFrameOcio(
            device,
            renderer,
            color,
            depth,
            width,
            height,
            renderSettings,
            ocioProcessor,
            result,
            page.Revision,
            page.CommandCount,
            renderer.GpuResources.Diagnostics);
    }

    private static ISilkGraphicsTexture CreateColorTarget(
        ISilkGraphicsDevice device,
        int width,
        int height) =>
        device.CreateTexture2D(
            SilkTextureDescriptor.HdrColorTarget(
                checked((uint)width),
                checked((uint)height)));

    private static SilkMeshRenderOptions CreateRenderOptions(RenderSettings renderSettings) =>
        new SilkMeshRenderOptions(
            new SilkColor(
                renderSettings.ClearColor.X,
                renderSettings.ClearColor.Y,
                renderSettings.ClearColor.Z,
                renderSettings.ClearColor.W),
            1,
            renderSettings.BackfaceCulling,
            renderSettings.UseSceneMaterials)
        {
            OutputTransform = RenderOutputTransform.Identity,
            Exposure = 0,
        };

    private static SilkMeshRenderOptions CreateDisplayRenderOptions(
        RenderSettings renderSettings) =>
        CreateRenderOptions(renderSettings) with
        {
            OutputTransform = renderSettings.OutputTransform,
            Exposure = renderSettings.Exposure,
        };

    private static SilkMeshRenderOptions CreateDisplayTransformRenderOptions(
        RenderSettings renderSettings) =>
        CreateRenderOptions(renderSettings) with
        {
            DisplayTransform = renderSettings.DisplayTransform,
            Exposure = renderSettings.Exposure,
        };

    private static SilkFrameCaptureResult ReadbackDisplayTransformedFrame(
        SilkMeshRenderer renderer,
        ISilkGraphicsTexture display,
        ISilkGraphicsTexture depth,
        int width,
        int height,
        SilkMeshRenderResult result,
        ulong pageRevision,
        uint commandCount)
    {
        // The GPU already produced display-referred RGBA8, so there is no CPU
        // conversion here at all. Running one would apply the display transform a
        // second time, which is exactly the double conversion the settings validation
        // exists to prevent.
        byte[] rgba = new byte[checked(width * height * 4)];
        display.ReadbackForTesting(rgba);
        if (renderer.Selection.Items.Count != 0 &&
            renderer.SelectionOutlineSettings.Enabled &&
            renderer.TryRenderDisplaySelectionOutline(display, depth))
        {
            display.ReadbackForTesting(rgba);
        }
        return new SilkFrameCaptureResult(
            width,
            height,
            rgba,
            result,
            pageRevision,
            commandCount,
            MergeDisplayTransformDiagnostics(renderer));
    }

    private static RenderDiagnosticsState MergeDisplayTransformDiagnostics(
        SilkMeshRenderer renderer)
    {
        RenderDiagnosticsState diagnostics = renderer.GpuResources.Diagnostics;
        RenderDiagnostic? displayDiagnostic = renderer.DisplayTransformDiagnostic;
        if (displayDiagnostic is null)
        {
            return diagnostics;
        }

        // Appended rather than replacing, and bounded to exactly one entry, so a
        // colour-management failure is visible next to the material and texture
        // degradations a caller already reads without displacing any of them.
        var entries = new List<RenderDiagnostic>(diagnostics.Entries.Count + 1);
        entries.AddRange(diagnostics.Entries);
        entries.Add(displayDiagnostic);
        return new RenderDiagnosticsState(entries);
    }

    private static SilkFrameCaptureResult ReadbackFrame(
        ISilkGraphicsDevice device,
        SilkMeshRenderer renderer,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        int width,
        int height,
        RenderSettings renderSettings,
        SilkMeshRenderResult result,
        ulong pageRevision,
        uint commandCount,
        RenderDiagnosticsState diagnostics)
    {
        byte[] linearRgba16 = new byte[checked(width * height * 8)];
        color.ReadbackForTesting(linearRgba16);
        byte[] rgba = new byte[checked(width * height * 4)];
        SilkDisplayConverter.ConvertRgba16FloatToRgba8(
            linearRgba16,
            rgba,
            renderSettings.OutputTransform,
            renderSettings.Exposure);
        if (renderer.Selection.Items.Count != 0 &&
            renderer.SelectionOutlineSettings.Enabled)
        {
            using ISilkGraphicsTexture display = CreateDisplayTarget(device, width, height);
            UploadDisplayImage(device, display, rgba);
            if (renderer.TryRenderDisplaySelectionOutline(display, depth))
            {
                display.ReadbackForTesting(rgba);
            }
        }
        return new SilkFrameCaptureResult(
            width,
            height,
            rgba,
            result,
            pageRevision,
            commandCount,
            diagnostics);
    }

    private static SilkFrameCaptureResult ReadbackFrameOcio(
        ISilkGraphicsDevice device,
        SilkMeshRenderer renderer,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        int width,
        int height,
        RenderSettings renderSettings,
        SilkOpenColorIoProcessor ocioProcessor,
        SilkMeshRenderResult result,
        ulong pageRevision,
        uint commandCount,
        RenderDiagnosticsState diagnostics)
    {
        byte[] linearRgba16 = new byte[checked(width * height * 8)];
        color.ReadbackForTesting(linearRgba16);
        byte[] rgba = new byte[checked(width * height * 4)];
        ocioProcessor.Apply(linearRgba16, rgba, width, height, renderSettings.Exposure);
        if (renderer.Selection.Items.Count != 0 &&
            renderer.SelectionOutlineSettings.Enabled)
        {
            using ISilkGraphicsTexture display = CreateDisplayTarget(device, width, height);
            UploadDisplayImage(device, display, rgba);
            if (renderer.TryRenderDisplaySelectionOutline(display, depth))
            {
                display.ReadbackForTesting(rgba);
            }
        }
        return new SilkFrameCaptureResult(
            width,
            height,
            rgba,
            result,
            pageRevision,
            commandCount,
            diagnostics);
    }

    internal static void ValidateOcioSettings(RenderSettings renderSettings)
    {
        if (renderSettings.OutputTransform != RenderOutputTransform.Identity)
        {
            throw new InvalidOperationException(
                "RenderSettings.OutputTransform must be Identity when an OCIO processor is " +
                "supplied. A non-Identity output transform would double-transform the image.");
        }
        if (renderSettings.DisplayTransform is not null)
        {
            throw new InvalidOperationException(
                "RenderSettings.DisplayTransform must be null when a CPU OCIO processor is " +
                "supplied. The GPU display transform and the CPU export processor are two " +
                "conversions of the same image, and running both would apply colour " +
                "management twice.");
        }
    }

    private static SilkFrameCaptureResult CaptureCustomRenderer(
        ISilkRenderTargetRenderer renderer,
        ISilkGraphicsDevice device,
        int width,
        int height,
        RenderSettings renderSettings,
        ulong pageRevision)
    {
        using ISilkGraphicsTexture color = CreateLegacyColorTarget(device, width, height);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(
                checked((uint)width),
                checked((uint)height)));
        SilkMeshRenderResult result = renderer.Render(
            color,
            depth,
            CreateDisplayRenderOptions(renderSettings));
        byte[] rgba = new byte[checked(width * height * 4)];
        color.ReadbackForTesting(rgba);
        return new SilkFrameCaptureResult(
            width,
            height,
            rgba,
            result,
            pageRevision,
            commandCount: 0,
            RenderDiagnosticsState.Empty);
    }

    private static ISilkGraphicsTexture CreateLegacyColorTarget(
        ISilkGraphicsDevice device,
        int width,
        int height) =>
        device.CreateTexture2D(
            new SilkTextureDescriptor(
                checked((uint)width),
                checked((uint)height),
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget |
                SilkTextureUsage.CopySource));

    private static ISilkGraphicsTexture CreateDisplayTarget(
        ISilkGraphicsDevice device,
        int width,
        int height) =>
        device.CreateTexture2D(
            new SilkTextureDescriptor(
                checked((uint)width),
                checked((uint)height),
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget |
                SilkTextureUsage.CopySource |
                SilkTextureUsage.CopyDestination));

    private static void UploadDisplayImage(
        ISilkGraphicsDevice device,
        ISilkGraphicsTexture display,
        ReadOnlySpan<byte> rgba)
    {
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.UploadTexture(display, rgba);
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        submission.Wait();
    }
}
