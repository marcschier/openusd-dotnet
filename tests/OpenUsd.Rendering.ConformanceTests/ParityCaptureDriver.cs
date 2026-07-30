// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Rendering.ConformanceTests;

internal sealed record ParityCaptureInput(
    string PluginPath,
    string StagePath,
    int Width,
    int Height,
    double TimeCode,
    CameraState Camera,
    SilkColor ClearColor,
    RenderHeadlight Headlight)
{
    internal uint BackgroundRgba => PackRgba(ClearColor);

    private static uint PackRgba(SilkColor color)
    {
        static byte convertChannel(float value) =>
            (byte)Math.Clamp((int)MathF.Round(value * byte.MaxValue), byte.MinValue, byte.MaxValue);

        return ((uint)convertChannel(color.Red) << 24) |
            ((uint)convertChannel(color.Green) << 16) |
            ((uint)convertChannel(color.Blue) << 8) |
            convertChannel(color.Alpha);
    }
}

internal sealed record SilkParityBackend(
    string Name,
    Func<ISilkGraphicsDevice> CreateDevice);

internal sealed record SilkParityCapture(
    string BackendName,
    ParityImage Image,
    int DrawCount,
    ulong Revision);

internal sealed record ParityCaptureSet(
    ParityImage Storm,
    IReadOnlyList<SilkParityCapture> SilkCaptures);

internal interface IStormGlContextFactory
{
    IStormGlContext Create(int width, int height, SilkColor clearColor);
}

internal interface IStormGlContext : IDisposable
{
    uint Framebuffer { get; }

    ParityImage ReadTopDownRgba();

    void Clear(SilkColor clearColor);

    void Finish();
}

internal static class ParityCaptureDriver
{
    internal static async Task<ParityCaptureSet> CaptureAsync(
        ParityCaptureInput input,
        IStormGlContextFactory stormContextFactory,
        IReadOnlyList<SilkParityBackend> silkBackends)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(stormContextFactory);
        ArgumentNullException.ThrowIfNull(silkBackends);
        ValidateInput(input);
        ValidateHeadlight(input.Headlight);

        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(input.StagePath);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync().ConfigureAwait(false);
        ParityImage storm = CaptureStorm(input, stormContextFactory, source);
        var silkCaptures = new List<SilkParityCapture>(silkBackends.Count);
        foreach (SilkParityBackend backend in silkBackends)
        {
            silkCaptures.Add(CaptureSilk(input, source, backend));
        }

        return new ParityCaptureSet(storm, silkCaptures);
    }

    private static ParityImage CaptureStorm(
        ParityCaptureInput input,
        IStormGlContextFactory stormContextFactory,
        UsdStageRenderSource source)
    {
        using IStormGlContext context = stormContextFactory.Create(
            input.Width,
            input.Height,
            input.ClearColor);
        using OpenUsdStormRenderer renderer = OpenUsdStormRuntime.Create(input.PluginPath, source);
        context.Clear(input.ClearColor);
        _ = renderer.Render(
            input.Width,
            input.Height,
            context.Framebuffer,
            input.TimeCode,
            input.Camera);
        context.Finish();
        return NormalizeCapture(context.ReadTopDownRgba(), input.ClearColor);
    }

    private static SilkParityCapture CaptureSilk(
        ParityCaptureInput input,
        UsdStageRenderSource source,
        SilkParityBackend backend)
    {
        using ISilkGraphicsDevice device = backend.CreateDevice();
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            new SilkTextureDescriptor(
                checked((uint)input.Width),
                checked((uint)input.Height),
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(
                checked((uint)input.Width),
                checked((uint)input.Height)));
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(input.PluginPath, source);
        using OpenUsdSilkPage page = session.Sync(
            input.Width,
            input.Height,
            input.TimeCode,
            input.Camera);
        using var renderer = new SilkMeshRenderer(device);
        SilkMeshRenderResult result = renderer.ApplyAndRender(
            page,
            color,
            depth,
            new SilkMeshRenderOptions(input.ClearColor, 1));
        byte[] pixels = new byte[input.Width * input.Height * ParityImage.BytesPerPixel];
        color.ReadbackForTesting(pixels);
        return new SilkParityCapture(
            backend.Name,
            NormalizeCapture(new ParityImage(input.Width, input.Height, pixels), input.ClearColor),
            result.DrawCount,
            page.Revision);
    }

    private static ParityImage NormalizeCapture(ParityImage image, SilkColor clearColor)
    {
        byte[] pixels = image.Rgba.ToArray();
        Span<byte> target = stackalloc byte[ParityImage.BytesPerPixel];
        WriteRgba(clearColor, target);
        Span<byte> source = stackalloc byte[ParityImage.BytesPerPixel];
        pixels.AsSpan(0, ParityImage.BytesPerPixel).CopyTo(source);
        for (int offset = 3; offset < pixels.Length; offset += ParityImage.BytesPerPixel)
        {
            pixels[offset] = byte.MaxValue;
        }

        for (int offset = 0; offset < pixels.Length; offset += ParityImage.BytesPerPixel)
        {
            if (MatchesBackground(pixels.AsSpan(offset, ParityImage.BytesPerPixel), source))
            {
                target.CopyTo(pixels.AsSpan(offset, ParityImage.BytesPerPixel));
            }
        }

        return new ParityImage(image.Width, image.Height, pixels);
    }

    private static bool MatchesBackground(ReadOnlySpan<byte> pixel, ReadOnlySpan<byte> background)
    {
        const int tolerance = 2;
        return Math.Abs(pixel[0] - background[0]) <= tolerance &&
            Math.Abs(pixel[1] - background[1]) <= tolerance &&
            Math.Abs(pixel[2] - background[2]) <= tolerance;
    }

    private static void WriteRgba(SilkColor color, Span<byte> destination)
    {
        destination[0] = convertChannel(color.Red);
        destination[1] = convertChannel(color.Green);
        destination[2] = convertChannel(color.Blue);
        destination[3] = byte.MaxValue;

        static byte convertChannel(float value) =>
            (byte)Math.Clamp((int)MathF.Round(value * byte.MaxValue), byte.MinValue, byte.MaxValue);
    }

    private static void ValidateInput(ParityCaptureInput input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.PluginPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.StagePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(input.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(input.Height);
        if (!File.Exists(input.StagePath))
        {
            throw new FileNotFoundException("The parity stage does not exist.", input.StagePath);
        }
    }

    private static void ValidateHeadlight(RenderHeadlight headlight)
    {
        RenderHeadlight storm = OpenUsdStormRuntime.Headlight;
        if (storm != headlight)
        {
            throw new InvalidOperationException(
                $"Parity captures require the Storm headlight convention; got {headlight} but Storm reports {storm}.");
        }
    }
}
