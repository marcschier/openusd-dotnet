// Copyright (c) marcschier. Licensed under the MIT License.

using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace OpenUsd.D3D12CompositionSmoke;

internal readonly record struct PixelCaptureRectangle(int X, int Y, int Width, int Height);

internal sealed record WindowsClientCaptureResult(
    SmokePixelCaptureEvidence Evidence,
    byte[] BgraPixels)
{
    internal static (long ChangedPixels, double MeanAbsoluteChannelDelta) Compare(
        WindowsClientCaptureResult before,
        WindowsClientCaptureResult after)
    {
        if (before.Evidence.Width != after.Evidence.Width ||
            before.Evidence.Height != after.Evidence.Height)
        {
            throw new InvalidOperationException(
                "Composed before/after captures must have identical viewport dimensions.");
        }

        long changed = 0;
        long totalDelta = 0;
        for (int offset = 0; offset < before.BgraPixels.Length; offset += 4)
        {
            int blue = Math.Abs(before.BgraPixels[offset] - after.BgraPixels[offset]);
            int green = Math.Abs(before.BgraPixels[offset + 1] - after.BgraPixels[offset + 1]);
            int red = Math.Abs(before.BgraPixels[offset + 2] - after.BgraPixels[offset + 2]);
            totalDelta += blue + green + red;
            if (Math.Max(red, Math.Max(green, blue)) >= 8)
            {
                changed++;
            }
        }
        return (
            changed,
            totalDelta / (double)(before.Evidence.Width * before.Evidence.Height * 3));
    }
}

internal static partial class WindowsClientCapture
{
    private const uint BiRgb = 0;
    private const uint DibRgbColors = 0;
    private const uint PwClientOnly = 0x1;
    private const uint PwRenderFullContent = 0x2;
    internal static WindowsClientCaptureResult Capture(
        nint window,
        PixelCaptureRectangle viewport,
        string phase,
        string bitmapPath)
    {
        if (window == 0)
        {
            throw new InvalidOperationException("Avalonia did not expose a Windows HWND.");
        }
        ThrowIfFalse(GetClientRect(window, out NativeRect client), "GetClientRect");
        int clientWidth = checked(client.Right - client.Left);
        int clientHeight = checked(client.Bottom - client.Top);
        ValidateViewport(viewport, clientWidth, clientHeight);
        ThrowIfFailed(DwmFlush(), "DwmFlush");

        nint memoryDcValue = CreateCompatibleDC(0);
        if (memoryDcValue == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateCompatibleDC failed.");
        }
        using SafeGdiDcHandle memoryDc = SafeGdiDcHandle.FromOwned(memoryDcValue);
        var bitmapInfo = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = checked((uint)Marshal.SizeOf<BitmapInfoHeader>()),
                Width = clientWidth,
                Height = -clientHeight,
                Planes = 1,
                BitCount = 32,
                Compression = BiRgb
            }
        };
        nint bitmapValue = CreateDIBSection(
            memoryDc,
            in bitmapInfo,
            DibRgbColors,
            out nint pixels,
            0,
            0);
        if (bitmapValue == 0 || pixels == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateDIBSection failed.");
        }
        using SafeGdiObjectHandle bitmap = SafeGdiObjectHandle.FromOwned(bitmapValue);

        nint previous = SelectObject(memoryDc, bitmap);
        if (previous == 0 || previous == -1)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "SelectObject failed.");
        }
        try
        {
            ThrowIfFalse(
                PrintWindow(window, memoryDc, PwClientOnly | PwRenderFullContent),
                "PrintWindow");
            ThrowIfFailed(DwmFlush(), "DwmFlush");
            byte[] clientPixels = new byte[checked(clientWidth * clientHeight * 4)];
            Marshal.Copy(pixels, clientPixels, 0, clientPixels.Length);
            byte[] viewportPixels = Crop(clientPixels, clientWidth, viewport);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(bitmapPath))!);
            WriteBitmap(bitmapPath, viewport.Width, viewport.Height, viewportPixels);
            return Analyze(phase, viewport.Width, viewport.Height, viewportPixels);
        }
        finally
        {
            _ = SelectObject(memoryDc, previous);
        }
    }

    private static WindowsClientCaptureResult Analyze(
        string phase,
        int width,
        int height,
        byte[] pixels)
    {
        var frequencies = new Dictionary<uint, int>();
        long redTotal = 0;
        long greenTotal = 0;
        long blueTotal = 0;
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            byte blue = pixels[offset];
            byte green = pixels[offset + 1];
            byte red = pixels[offset + 2];
            uint color = (uint)(blue | green << 8 | red << 16 | pixels[offset + 3] << 24);
            frequencies[color] = frequencies.GetValueOrDefault(color) + 1;
            redTotal += red;
            greenTotal += green;
            blueTotal += blue;
        }
        uint background = frequencies.MaxBy(pair => pair.Value).Key;
        byte backgroundBlue = (byte)background;
        byte backgroundGreen = (byte)(background >> 8);
        byte backgroundRed = (byte)(background >> 16);
        long nonBackground = 0;
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            int delta = Math.Max(
                Math.Abs(pixels[offset] - backgroundBlue),
                Math.Max(
                    Math.Abs(pixels[offset + 1] - backgroundGreen),
                    Math.Abs(pixels[offset + 2] - backgroundRed)));
            if (delta >= 12)
            {
                nonBackground++;
            }
        }

        long pixelCount = width * (long)height;
        string hash = Convert.ToHexString(SHA256.HashData(pixels));
        string[] samples =
        [
            Sample(pixels, width, width / 4, height / 4),
            Sample(pixels, width, width / 2, height / 4),
            Sample(pixels, width, width * 3 / 4, height / 2),
            Sample(pixels, width, width / 2, height / 2),
            Sample(pixels, width, width / 4, height * 3 / 4),
            Sample(pixels, width, width * 3 / 4, height * 3 / 4)
        ];
        var evidence = new SmokePixelCaptureEvidence(
            phase,
            hash,
            width,
            height,
            background.ToString("X8", CultureInfo.InvariantCulture),
            nonBackground,
            redTotal / (double)pixelCount,
            greenTotal / (double)pixelCount,
            blueTotal / (double)pixelCount,
            samples);
        return new WindowsClientCaptureResult(evidence, pixels);
    }

    private static byte[] Crop(
        byte[] clientPixels,
        int clientWidth,
        PixelCaptureRectangle viewport)
    {
        int rowBytes = checked(viewport.Width * 4);
        byte[] result = new byte[checked(rowBytes * viewport.Height)];
        for (int row = 0; row < viewport.Height; row++)
        {
            Buffer.BlockCopy(
                clientPixels,
                checked(((viewport.Y + row) * clientWidth + viewport.X) * 4),
                result,
                row * rowBytes,
                rowBytes);
        }
        return result;
    }

    private static string Sample(byte[] pixels, int width, int x, int y)
    {
        int offset = checked((y * width + x) * 4);
        return Convert.ToHexString(pixels.AsSpan(offset, 4));
    }

    private static void WriteBitmap(string path, int width, int height, byte[] pixels)
    {
        const int fileHeaderSize = 14;
        const int infoHeaderSize = 40;
        int dataOffset = fileHeaderSize + infoHeaderSize;
        using FileStream stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0x4D42);
        writer.Write(checked(dataOffset + pixels.Length));
        writer.Write(0);
        writer.Write(dataOffset);
        writer.Write(infoHeaderSize);
        writer.Write(width);
        writer.Write(-height);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(BiRgb);
        writer.Write(pixels.Length);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(pixels);
    }

    private static void ValidateViewport(
        PixelCaptureRectangle viewport,
        int clientWidth,
        int clientHeight)
    {
        if (viewport.X < 0 ||
            viewport.Y < 0 ||
            viewport.Width <= 0 ||
            viewport.Height <= 0 ||
            viewport.X + viewport.Width > clientWidth ||
            viewport.Y + viewport.Height > clientHeight)
        {
            throw new InvalidOperationException(
                $"Viewport crop {viewport} is outside client area {clientWidth}x{clientHeight}.");
        }
    }

    private static void ThrowIfFalse(int result, string operation)
    {
        if (result == 0)
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                $"{operation} failed; composed pixel evidence is unavailable.");
        }
    }

    private static void ThrowIfFailed(int hresult, string operation)
    {
        if (hresult < 0)
        {
            throw new InvalidOperationException(
                $"{operation} failed with HRESULT 0x{hresult:X8}.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        internal uint Size;
        internal int Width;
        internal int Height;
        internal ushort Planes;
        internal ushort BitCount;
        internal uint Compression;
        internal uint SizeImage;
        internal int XPelsPerMeter;
        internal int YPelsPerMeter;
        internal uint ColorsUsed;
        internal uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        internal BitmapInfoHeader Header;
        internal uint Colors;
    }

    private sealed class SafeGdiDcHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeGdiDcHandle()
            : base(ownsHandle: true)
        {
        }

        internal static SafeGdiDcHandle FromOwned(nint handle)
        {
            var result = new SafeGdiDcHandle();
            result.SetHandle(handle);
            return result;
        }

        protected override bool ReleaseHandle() => DeleteDC(handle) != 0;
    }

    private sealed class SafeGdiObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeGdiObjectHandle()
            : base(ownsHandle: true)
        {
        }

        internal static SafeGdiObjectHandle FromOwned(nint handle)
        {
            var result = new SafeGdiObjectHandle();
            result.SetHandle(handle);
            return result;
        }

        protected override bool ReleaseHandle() => DeleteObject(handle) != 0;
    }

    [LibraryImport("user32", SetLastError = true)]
    private static partial int GetClientRect(nint window, out NativeRect rectangle);

    [LibraryImport("user32", SetLastError = true)]
    private static partial int PrintWindow(nint window, SafeGdiDcHandle deviceContext, uint flags);

    [LibraryImport("gdi32", SetLastError = true)]
    private static partial nint CreateCompatibleDC(nint deviceContext);

    [LibraryImport("gdi32", SetLastError = true)]
    private static partial nint CreateDIBSection(
        SafeGdiDcHandle deviceContext,
        in BitmapInfo bitmapInfo,
        uint usage,
        out nint pixels,
        nint section,
        uint offset);

    [LibraryImport("gdi32", SetLastError = true)]
    private static partial nint SelectObject(
        SafeGdiDcHandle deviceContext,
        SafeGdiObjectHandle gdiObject);

    [LibraryImport("gdi32", SetLastError = true)]
    private static partial nint SelectObject(SafeGdiDcHandle deviceContext, nint gdiObject);

    [LibraryImport("gdi32")]
    private static partial int DeleteDC(nint deviceContext);

    [LibraryImport("gdi32")]
    private static partial int DeleteObject(nint gdiObject);

    [LibraryImport("dwmapi")]
    private static partial int DwmFlush();
}
