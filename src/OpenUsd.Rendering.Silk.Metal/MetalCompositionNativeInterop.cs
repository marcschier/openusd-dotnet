// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using SharpMetal.ObjectiveCCore;

namespace OpenUsd.Rendering.Silk.Metal;

internal static partial class MetalCompositionNativeInterop
{
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string IOSurface =
        "/System/Library/Frameworks/IOSurface.framework/IOSurface";
    private const int CfNumberSInt64Type = 4;
    private const long RgbaPixelFormat = 0x52474241;
    private static readonly Lazy<IOSurfaceSymbols> Symbols = new(LoadSymbols);

    internal static IOSurfaceHandle CreateIOSurface(uint width, uint height)
    {
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);

        IOSurfaceSymbols symbols = Symbols.Value;
        nuint bytesPerRow = IOSurfaceAlignProperty(
            symbols.BytesPerRow,
            checked((nuint)width * 4));
        nuint allocationSize = IOSurfaceAlignProperty(
            symbols.AllocationSize,
            checked((nuint)height * bytesPerRow));
        nint[] keys =
        [
            symbols.Width,
            symbols.Height,
            symbols.PixelFormat,
            symbols.BytesPerElement,
            symbols.BytesPerRow,
            symbols.AllocationSize,
            symbols.ElementWidth,
            symbols.ElementHeight
        ];
        long[] rawValues =
        [
            width,
            height,
            RgbaPixelFormat,
            4,
            checked((long)bytesPerRow),
            checked((long)allocationSize),
            1,
            1
        ];
        nint[] values = new nint[rawValues.Length];
        nint dictionary = 0;
        try
        {
            unsafe
            {
                for (int index = 0; index < rawValues.Length; index++)
                {
                    long value = rawValues[index];
                    values[index] = CFNumberCreate(0, CfNumberSInt64Type, &value);
                    if (values[index] == 0)
                    {
                        throw new InvalidOperationException(
                            "Could not create an IOSurface property value.");
                    }
                }

                fixed (nint* keyPointer = keys)
                fixed (nint* valuePointer = values)
                {
                    dictionary = CFDictionaryCreate(
                        0,
                        keyPointer,
                        valuePointer,
                        keys.Length,
                        symbols.DictionaryKeyCallbacks,
                        symbols.DictionaryValueCallbacks);
                }
            }
            if (dictionary == 0)
            {
                throw new InvalidOperationException(
                    "Could not create the IOSurface property dictionary.");
            }

            nint surface = IOSurfaceCreate(dictionary);
            if (surface == 0)
            {
                throw new PlatformNotSupportedException(
                    "IOSurfaceCreate did not provide an IOSurface.");
            }
            return new IOSurfaceHandle(surface, allocationSize);
        }
        finally
        {
            if (dictionary != 0)
            {
                CFRelease(dictionary);
            }
            foreach (nint value in values)
            {
                if (value != 0)
                {
                    CFRelease(value);
                }
            }
        }
    }

    internal static SafeCFReferenceHandle RetainIOSurface(IOSurfaceHandle surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ObjectDisposedException.ThrowIf(surface.IsClosed || surface.IsInvalid, surface);
        nint retained = CFRetain(surface.DangerousGetHandle());
        if (retained == 0)
        {
            throw new InvalidOperationException("Could not retain the IOSurface.");
        }
        return new SafeCFReferenceHandle(retained);
    }

    [SupportedOSPlatform("macos")]
    internal static SafeObjectiveCReferenceHandle RetainObjectiveCObject(nint handle)
    {
        ArgumentOutOfRangeException.ThrowIfZero(handle);
        nint retained = ObjectiveC.IntPtr_objc_msgSend(handle, new Selector("retain"));
        if (retained == 0)
        {
            throw new InvalidOperationException("Could not retain the Metal shared event.");
        }
        return new SafeObjectiveCReferenceHandle(retained);
    }

    internal static void ReleaseCFObject(nint handle) => CFRelease(handle);

    private static IOSurfaceSymbols LoadSymbols()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("IOSurface is available only on macOS.");
        }

        nint ioSurfaceLibrary = NativeLibrary.Load(IOSurface);
        nint coreFoundationLibrary = NativeLibrary.Load(CoreFoundation);
        return new IOSurfaceSymbols(
            ReadExportedObject(ioSurfaceLibrary, "kIOSurfaceWidth"),
            ReadExportedObject(ioSurfaceLibrary, "kIOSurfaceHeight"),
            ReadExportedObject(ioSurfaceLibrary, "kIOSurfacePixelFormat"),
            ReadExportedObject(ioSurfaceLibrary, "kIOSurfaceBytesPerElement"),
            ReadExportedObject(ioSurfaceLibrary, "kIOSurfaceBytesPerRow"),
            ReadExportedObject(ioSurfaceLibrary, "kIOSurfaceAllocSize"),
            ReadExportedObject(ioSurfaceLibrary, "kIOSurfaceElementWidth"),
            ReadExportedObject(ioSurfaceLibrary, "kIOSurfaceElementHeight"),
            NativeLibrary.GetExport(
                coreFoundationLibrary,
                "kCFTypeDictionaryKeyCallBacks"),
            NativeLibrary.GetExport(
                coreFoundationLibrary,
                "kCFTypeDictionaryValueCallBacks"));
    }

    private static nint ReadExportedObject(nint library, string name)
    {
        nint address = NativeLibrary.GetExport(library, name);
        nint value = Marshal.ReadIntPtr(address);
        if (value == 0)
        {
            throw new PlatformNotSupportedException(
                $"The IOSurface symbol '{name}' was unavailable.");
        }
        return value;
    }

    [LibraryImport(CoreFoundation, EntryPoint = "CFDictionaryCreate")]
    private static unsafe partial nint CFDictionaryCreate(
        nint allocator,
        nint* keys,
        nint* values,
        nint count,
        nint keyCallbacks,
        nint valueCallbacks);

    [LibraryImport(CoreFoundation, EntryPoint = "CFNumberCreate")]
    private static unsafe partial nint CFNumberCreate(
        nint allocator,
        int numberType,
        void* value);

    [LibraryImport(CoreFoundation, EntryPoint = "CFRetain")]
    private static partial nint CFRetain(nint value);

    [LibraryImport(CoreFoundation, EntryPoint = "CFRelease")]
    private static partial void CFRelease(nint value);

    [LibraryImport(IOSurface, EntryPoint = "IOSurfaceAlignProperty")]
    private static partial nuint IOSurfaceAlignProperty(nint property, nuint value);

    [LibraryImport(IOSurface, EntryPoint = "IOSurfaceCreate")]
    private static partial nint IOSurfaceCreate(nint properties);

    private sealed record IOSurfaceSymbols(
        nint Width,
        nint Height,
        nint PixelFormat,
        nint BytesPerElement,
        nint BytesPerRow,
        nint AllocationSize,
        nint ElementWidth,
        nint ElementHeight,
        nint DictionaryKeyCallbacks,
        nint DictionaryValueCallbacks);
}

internal sealed class IOSurfaceHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal IOSurfaceHandle(nint handle, nuint allocationSize)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
        AllocationSize = allocationSize;
    }

    internal nuint AllocationSize { get; }

    protected override bool ReleaseHandle()
    {
        MetalCompositionNativeInterop.ReleaseCFObject(handle);
        return true;
    }
}

internal sealed class SafeCFReferenceHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeCFReferenceHandle(nint handle)
        : base(ownsHandle: true) =>
        SetHandle(handle);

    protected override bool ReleaseHandle()
    {
        MetalCompositionNativeInterop.ReleaseCFObject(handle);
        return true;
    }
}

[SupportedOSPlatform("macos")]
internal sealed class SafeObjectiveCReferenceHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeObjectiveCReferenceHandle(nint handle)
        : base(ownsHandle: true) =>
        SetHandle(handle);

    protected override bool ReleaseHandle()
    {
        ObjectiveC.objc_msgSend(handle, new Selector("release"));
        return true;
    }
}
