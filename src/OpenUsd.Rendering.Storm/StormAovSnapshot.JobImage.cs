// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;

namespace OpenUsd.Rendering.Storm;

public sealed partial class StormAovSnapshot
{
    private static readonly RenderDiagnosticsState JobImageDiagnostics = new(
    [
        new RenderDiagnostic(RenderDiagnosticSeverity.Information, "STORM_AOV_JOB_IMAGE",
            "Display pixels were converted from native Storm half-color AOVs. Optional raw color retains " +
            "native working values and alpha only from captures without display selection; depth retains " +
            "normalized OpenGL window values, not metric distance. " +
            "Other requested AOVs and canonical identities are not exported by this image conversion.")
    ]);

    private static readonly RenderDiagnosticsState NativeJobImageDiagnostics = new(
    [
        new RenderDiagnostic(RenderDiagnosticSeverity.Information, "STORM_CHILD_AOV_JOB_IMAGE",
            "PNG preserves native Storm viewport appearance without exposure, tone-map or OCIO conversion. " +
            "Optional raw half color and normalized OpenGL depth came from the same render-thread " +
            "operation without intervening stage changes. Raw HDR requires no display selection.")
    ]);

    /// <summary>Converts detached native color and optional depth into the shared disk-job image shape.</summary>
    /// <param name="includeDeviceDepth">
    /// Copy the available normalized OpenGL depth plane without linearizing it.
    /// </param>
    /// <param name="includeHdrColor">Copy raw little-endian half RGBA, preserving all finite bits and alpha.</param>
    /// <param name="outputTransform">Explicit display-only transform; raw planes are unaffected.</param>
    /// <param name="exposure">
    /// Finite display-only RGB exposure in stops; alpha is not exposure-adjusted or tone-mapped.
    /// </param>
    /// <param name="maximumManagedBytes">
    /// Combined existing snapshot upper bound and new pixel storage, at most 64 MiB.
    /// </param>
    /// <param name="cancellationToken">Cancels before allocation or between bounded pixel batches.</param>
    /// <returns>An independent top-down display image and exactly the requested additional planes.</returns>
    /// <remarks>
    /// This makes no native calls, rerenders nothing and does not apply scene filters or other render settings.
    /// A frame source must match the snapshot's time and applied camera to its job request. No native ID or Neye
    /// output is substituted for color/depth. Raw HDR is refused if the capture had display selection, because
    /// Storm can bake highlights into its color AOV; this conversion never changes renderer selection.
    /// Conversion charges snapshot storage plus new rasters and a
    /// 512-byte bookkeeping allowance; native/GPU/codec working memory is outside that budget.
    /// </remarks>
    public RenderJobImage CreateJobImage(
        bool includeDeviceDepth = false,
        bool includeHdrColor = false,
        RenderOutputTransform outputTransform = RenderOutputTransform.Identity,
        float exposure = 0,
        long maximumManagedBytes = 64 * 1024 * 1024,
        CancellationToken cancellationToken = default) =>
        CreateJobImageCore(includeDeviceDepth, includeHdrColor, outputTransform, exposure,
            maximumManagedBytes, default, cancellationToken);

    internal RenderJobImage CreateJobImageWithNativePresentation(
        ReadOnlyMemory<byte> presentation,
        bool includeDeviceDepth,
        bool includeHdrColor,
        long maximumManagedBytes,
        CancellationToken cancellationToken)
    {
        if (presentation.Length != checked(Width * Height * 4))
        {
            throw new ArgumentException(
                "Native presentation storage does not match the AOV dimensions.", nameof(presentation));
        }
        return CreateJobImageCore(includeDeviceDepth, includeHdrColor, RenderOutputTransform.Identity, 0,
            maximumManagedBytes, presentation, cancellationToken);
    }

    private RenderJobImage CreateJobImageCore(
        bool includeDeviceDepth,
        bool includeHdrColor,
        RenderOutputTransform outputTransform,
        float exposure,
        long maximumManagedBytes,
        ReadOnlyMemory<byte> nativePresentation,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumManagedBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumManagedBytes, 64L * 1024 * 1024);
        cancellationToken.ThrowIfCancellationRequested();
        if (includeHdrColor && _hasDisplaySelection)
        {
            throw new NotSupportedException(
                "Raw HDR requires a Storm capture without display selection; " +
                "native highlights cannot be removed from the color AOV.");
        }
        float scale = Rgba16FloatDisplayConverter.GetExposureScale(outputTransform, exposure);
        int count = checked(Width * Height);
        bool nativeAppearance = !nativePresentation.IsEmpty;
        uint bytesPerPixel = 4u + (includeHdrColor ? 8u : 0) + (includeDeviceDepth ? 4u : 0);
        ulong companionStorage = nativeAppearance
            ? (ulong)nativePresentation.Length + OpenUsdStormChildAovCapture.ManagedCompanionAllowance : 0;
        ulong storage = checked(ManagedStorageUpperBound + companionStorage + 512 + (ulong)count * bytesPerPixel);
        if (storage > (ulong)maximumManagedBytes)
        {
            throw new RenderOutputQuotaExceededException(
                "The Storm snapshot and requested job planes exceed the managed conversion budget.");
        }
        StormAovOutput<StormAovColor>? color = !nativeAppearance || includeHdrColor
            ? RequireImageOutput<StormAovColor>(StormAovKind.Color, StormAovFormat.Float16Vec4, count) : null;
        StormAovOutput<float>? depth = includeDeviceDepth
            ? RequireImageOutput<float>(StormAovKind.Depth, StormAovFormat.Float32, count) : null;
        if (depth is not null && depth.DepthConvention != StormAovDepthConvention.OpenGlWindow)
        {
            throw new InvalidDataException("The captured depth does not declare normalized OpenGL window values.");
        }

        byte[] rgba = nativeAppearance ? nativePresentation.ToArray() : new byte[checked(count * 4)];
        byte[]? hdr = includeHdrColor ? new byte[checked(count * 8)] : null;
        float[]? depthValues = includeDeviceDepth ? new float[count] : null;
        for (int index = 0; index < count; index++)
        {
            if ((index & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (color is not null)
            {
                StormAovColor pixel = color.Pixels[index];
                if (!Half.IsFinite(pixel.Red) || !Half.IsFinite(pixel.Green) ||
                    !Half.IsFinite(pixel.Blue) || !Half.IsFinite(pixel.Alpha))
                {
                    throw new InvalidDataException("The Storm color AOV contains a non-finite channel.");
                }
                if (!nativeAppearance)
                {
                    int offset = index * 4;
                    rgba[offset] = Rgba16FloatDisplayConverter.Quantize(
                        Rgba16FloatDisplayConverter.Transform((float)pixel.Red, scale, outputTransform));
                    rgba[offset + 1] = Rgba16FloatDisplayConverter.Quantize(
                        Rgba16FloatDisplayConverter.Transform((float)pixel.Green, scale, outputTransform));
                    rgba[offset + 2] = Rgba16FloatDisplayConverter.Quantize(
                        Rgba16FloatDisplayConverter.Transform((float)pixel.Blue, scale, outputTransform));
                    rgba[offset + 3] = Rgba16FloatDisplayConverter.Quantize((float)pixel.Alpha);
                }
                if (hdr is not null)
                {
                    Span<byte> raw = hdr.AsSpan(index * 8, 8);
                    BinaryPrimitives.WriteUInt16LittleEndian(raw, BitConverter.HalfToUInt16Bits(pixel.Red));
                    BinaryPrimitives.WriteUInt16LittleEndian(raw[2..], BitConverter.HalfToUInt16Bits(pixel.Green));
                    BinaryPrimitives.WriteUInt16LittleEndian(raw[4..], BitConverter.HalfToUInt16Bits(pixel.Blue));
                    BinaryPrimitives.WriteUInt16LittleEndian(raw[6..], BitConverter.HalfToUInt16Bits(pixel.Alpha));
                }
            }
            if (depth is not null && depthValues is not null)
            {
                float value = depth.Pixels[index];
                if (!float.IsFinite(value) || value is < 0 or > 1)
                {
                    throw new InvalidDataException(
                        "The Storm depth AOV contains a value outside normalized window depth.");
                }
                depthValues[index] = value;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new RenderJobImage(Width, Height, rgba,
            nativeAppearance ? Rgba8RowOrder.BottomUp : Rgba8RowOrder.TopDown)
        {
            HdrColor = hdr is null ? null : new RenderJobHdrColor(Width, Height, hdr),
            DeviceDepth = depthValues is null ? null : new RenderJobDeviceDepth(Width, Height, depthValues),
            Diagnostics = nativeAppearance ? NativeJobImageDiagnostics : JobImageDiagnostics
        };
    }

    private StormAovOutput<T> RequireImageOutput<T>(StormAovKind kind, StormAovFormat format, int count)
        where T : unmanaged
    {
        foreach (StormAovOutput output in Outputs)
        {
            if (output.Kind == kind)
            {
                if (output is StormAovOutput<T> typed &&
                    output.Status == StormAovStatus.Ready && output.Format == format &&
                    output.Origin == StormAovOrigin.TopLeft && output.Width == Width && output.Height == Height &&
                    typed.Pixels.Count == count)
                {
                    return typed;
                }
                break;
            }
        }
        throw new InvalidDataException(
            $"The snapshot does not contain an available {kind} output in its required representation.");
    }
}
