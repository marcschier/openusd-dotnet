// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using SharpMetal.Metal;

namespace OpenUsd.Rendering.Silk.Metal;

[SupportedOSPlatform("macos")]
public sealed partial class MetalSilkGraphicsDevice
{
    internal byte[] GetPresentationDeviceLuid()
    {
        byte[] identifier = BitConverter.GetBytes(_device.RegistryID);
        Array.Reverse(identifier);
        return identifier;
    }

    internal void ProbeCompositionPresentation()
    {
        using IOSurfaceHandle surface =
            MetalCompositionNativeInterop.CreateIOSurface(1, 1);
        using MetalSilkGraphicsTexture texture = CreateIOSurfaceTexture(
            surface,
            new SilkTextureDescriptor(
                1,
                1,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.Sampled));
        MTLSharedEvent sharedEvent = _device.NewSharedEvent();
        if (sharedEvent.NativePtr == 0)
        {
            throw new PlatformNotSupportedException(
                "The Metal device does not support MTLSharedEvent.");
        }
        try
        {
            MTLSharedEventHandle sharedHandle = sharedEvent.NewSharedEventHandle;
            if (sharedHandle.NativePtr == 0)
            {
                throw new PlatformNotSupportedException(
                    "The Metal device cannot export MTLSharedEvent handles.");
            }
            try
            {
                MTLSharedEvent imported = _device.NewSharedEvent(sharedHandle);
                if (imported.NativePtr == 0)
                {
                    throw new PlatformNotSupportedException(
                        "The Metal device cannot import MTLSharedEvent handles.");
                }
                imported.Dispose();
            }
            finally
            {
                sharedHandle.Dispose();
            }
        }
        finally
        {
            sharedEvent.Dispose();
        }
    }

    internal MetalSilkGraphicsTexture CreateIOSurfaceTexture(
        IOSurfaceHandle surface,
        SilkTextureDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(surface);
        descriptor.Validate();
        if (descriptor.Format != SilkTextureFormat.Rgba8Unorm ||
            !descriptor.Usage.HasFlag(SilkTextureUsage.ColorRenderTarget))
        {
            throw new ArgumentException(
                "IOSurface presentation textures must be RGBA8 color render targets.",
                nameof(descriptor));
        }

        RegisterDependentObject();
        MTLTextureDescriptor nativeDescriptor = default;
        MTLTexture texture = default;
        bool success = false;
        try
        {
            nativeDescriptor = MTLTextureDescriptor.Texture2DDescriptor(
                MTLPixelFormat.RGBA8Unorm,
                descriptor.Width,
                descriptor.Height,
                false);
            nativeDescriptor.StorageMode = MTLStorageMode.Shared;
            nativeDescriptor.Usage =
                MTLTextureUsage.RenderTarget | MTLTextureUsage.ShaderRead;
            texture = _device.NewTexture(
                nativeDescriptor,
                surface.DangerousGetHandle(),
                0);
            if (texture.NativePtr == 0 || texture.Iosurface == 0)
            {
                throw new PlatformNotSupportedException(
                    "The Metal device could not create an IOSurface-backed texture.");
            }
            success = true;
            return new MetalSilkGraphicsTexture(this, texture, descriptor);
        }
        finally
        {
            if (nativeDescriptor.NativePtr != 0)
            {
                nativeDescriptor.Dispose();
            }
            if (!success)
            {
                if (texture.NativePtr != 0)
                {
                    texture.Dispose();
                }
                ReleaseDependentObject();
            }
        }
    }

    internal MTLSharedEvent CreatePresentationSharedEvent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        MTLSharedEvent sharedEvent = _device.NewSharedEvent();
        if (sharedEvent.NativePtr == 0)
        {
            throw new PlatformNotSupportedException(
                "The Metal device could not create an MTLSharedEvent.");
        }
        sharedEvent.SignaledValue = 0;
        return sharedEvent;
    }

    internal MTLCommandBuffer SubmitEventSignal(MTLSharedEvent sharedEvent, ulong value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        MTLCommandBuffer commandBuffer = _queue.CommandBuffer();
        if (commandBuffer.NativePtr == 0)
        {
            throw new InvalidOperationException(
                "Could not create a Metal shared-event signal command buffer.");
        }
        try
        {
            commandBuffer.EncodeSignalEvent(sharedEvent, value);
            commandBuffer.Commit();
            return commandBuffer;
        }
        catch
        {
            commandBuffer.Dispose();
            throw;
        }
    }
}
