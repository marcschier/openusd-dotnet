// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;

namespace OpenUsd.Rendering;

internal readonly record struct RenderProductFraming(
    StageCameraApertureWindow Window,
    ViewportDimensions OutputDimensions,
    int DataWindowMinX,
    int DataWindowMinY,
    double PixelAspectRatio)
{
    internal static bool IsKnownConformPolicy(string value) => value is
        "expandAperture" or "cropAperture" or "adjustApertureWidth" or
        "adjustApertureHeight" or "adjustPixelAspectRatio";

    internal static RenderProductFraming Resolve(
        StageCameraApertureWindow authored,
        ViewportDimensions resolution,
        double pixelAspectRatio,
        string conformPolicy,
        Vector4 dataWindowNdc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resolution.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resolution.Height);
        if (!double.IsFinite(pixelAspectRatio) || pixelAspectRatio <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelAspectRatio), "Pixel aspect ratio must be positive.");
        }
        authored = StageCameraProjectionMath.CreateWindow(
            authored.Left, authored.Right, authored.Bottom, authored.Top);
        double imageAspect = (double)resolution.Width / resolution.Height;
        double authoredAspect = authored.Width / authored.Height;
        if (conformPolicy == "adjustPixelAspectRatio")
        {
            return ApplyDataWindow(authored, resolution, authoredAspect / imageAspect, dataWindowNdc);
        }
        double targetAspect = imageAspect * pixelAspectRatio;
        if (!double.IsFinite(targetAspect) || targetAspect <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelAspectRatio), "Image aspect ratio is not representable.");
        }

        bool adjustWidth = conformPolicy switch
        {
            "expandAperture" => targetAspect >= authoredAspect,
            "cropAperture" => targetAspect <= authoredAspect,
            "adjustApertureWidth" => true,
            "adjustApertureHeight" => false,
            _ => throw new ArgumentException(
                "Unknown render-product aspect-ratio conform policy.", nameof(conformPolicy))
        };
        StageCameraApertureWindow window;
        if (adjustWidth)
        {
            double change = (authored.Height * targetAspect - authored.Width) / 2;
            window = StageCameraProjectionMath.CreateWindow(
                authored.Left - change, authored.Right + change, authored.Bottom, authored.Top);
        }
        else
        {
            double change = (authored.Width / targetAspect - authored.Height) / 2;
            window = StageCameraProjectionMath.CreateWindow(
                authored.Left, authored.Right, authored.Bottom - change, authored.Top + change);
        }
        return ApplyDataWindow(window, resolution, pixelAspectRatio, dataWindowNdc);
    }

    private static RenderProductFraming ApplyDataWindow(
        StageCameraApertureWindow window,
        ViewportDimensions resolution,
        double pixelAspectRatio,
        Vector4 dataWindow)
    {
        if (!double.IsFinite(pixelAspectRatio) || pixelAspectRatio <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pixelAspectRatio), "Effective pixel aspect is not representable.");
        }
        if (!float.IsFinite(dataWindow.X) || !float.IsFinite(dataWindow.Y) ||
            !float.IsFinite(dataWindow.Z) || !float.IsFinite(dataWindow.W) ||
            dataWindow.X >= dataWindow.Z || dataWindow.Y >= dataWindow.W)
        {
            throw new ArgumentException("Data window bounds must be finite and increasing.", nameof(dataWindow));
        }

        int minX = PixelBoundary(dataWindow.X, resolution.Width);
        int minY = PixelBoundary(dataWindow.Y, resolution.Height);
        int maxX = PixelBoundary(dataWindow.Z, resolution.Width);
        int maxY = PixelBoundary(dataWindow.W, resolution.Height);
        long width = (long)maxX - minX;
        long height = (long)maxY - minY;
        if (width <= 0 || height <= 0 || width > int.MaxValue || height > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(dataWindow), "Data window has no representable pixel extent.");
        }
        StageCameraApertureWindow cropped = minX == 0 && minY == 0 &&
            maxX == resolution.Width && maxY == resolution.Height
            ? window
            : StageCameraProjectionMath.CreateWindow(
                window.Left + window.Width * ((double)minX / resolution.Width),
                window.Left + window.Width * ((double)maxX / resolution.Width),
                window.Bottom + window.Height * ((double)minY / resolution.Height),
                window.Bottom + window.Height * ((double)maxY / resolution.Height));
        return new RenderProductFraming(
            cropped, new ViewportDimensions((int)width, (int)height), minX, minY, pixelAspectRatio);
    }

    private static int PixelBoundary(float normalized, int extent)
    {
        // Pixels are selected by their centers: the minimum is inclusive and maximum exclusive.
        double boundary = Math.Ceiling((double)normalized * extent - 0.5);
        if (!double.IsFinite(boundary) || boundary < int.MinValue || boundary > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(normalized), "Data window pixel coordinates exceed Int32.");
        }
        return (int)boundary;
    }
}
