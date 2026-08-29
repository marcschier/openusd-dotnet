// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;

namespace OpenUsd.Rendering.Silk.Metal;

/// <summary>
/// Minimal Metal device, queue, command-buffer, and buffer implementation.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed partial class MetalSilkGraphicsDevice
    : SilkGraphicsDeviceLifetimeBase,
      ISilkGraphicsDevice,
      ISilkPickingGraphicsDevice,
      ISilkSelectionOutlineGraphicsDevice
{
    private readonly MTLArgumentBuffersTier _argumentBuffersSupport;
    private readonly MetalDescriptorIndexedTextureTables? _materialDescriptorTables;
    private MTLDevice _device;
    private MTLCommandQueue _queue;
    private bool _disposed;

    private MetalSilkGraphicsDevice(MTLDevice device, MTLCommandQueue queue)
    {
        _device = device;
        _queue = queue;
        _argumentBuffersSupport = device.ArgumentBuffersSupport;
        if (_argumentBuffersSupport == MTLArgumentBuffersTier.Tier2)
        {
            _materialDescriptorTables =
                new MetalDescriptorIndexedTextureTables(device);
        }
        Capabilities = new SilkGraphicsCapabilities(
            device.Name.ToString() ?? "Metal Device",
            "Metal",
            SupportsCompute: true,
            IsSoftware: false)
        {
            SupportsDescriptorIndexedTextureTables =
                _materialDescriptorTables is not null,
            DescriptorIndexedTextureTablesDiagnostic =
                _materialDescriptorTables is null
                    ? "Metal descriptor-indexed texture tables unavailable: " +
                        $"ArgumentBuffersSupport is {_argumentBuffersSupport}; requires Tier2."
                    : null,
            // Metal guarantees samplers accept a maxAnisotropy of up to 16 on every device --
            // there is no separate feature query in the current SharpMetal surface, so this
            // uses the API-documented ceiling rather than an unverifiable runtime probe.
            MaxSamplerAnisotropy = 16f
        };
    }

    /// <inheritdoc/>
    public SilkGraphicsBackend Backend => SilkGraphicsBackend.Metal;

    /// <inheritdoc/>
    public SilkGraphicsCapabilities Capabilities { get; }

    internal MTLArgumentBuffersTier ArgumentBuffersSupportForTesting =>
        _argumentBuffersSupport;

    internal MetalDescriptorIndexedTextureTables? MaterialDescriptorTables =>
        _materialDescriptorTables;

    /// <summary>Creates the system-default Metal device and command queue.</summary>
    public static MetalSilkGraphicsDevice Create()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Metal is available only on macOS.");
        }

        ObjectiveC.LinkMetal();
        MTLDevice device = MTLDevice.CreateSystemDefaultDevice();
        if (device.NativePtr == 0)
        {
            throw new PlatformNotSupportedException("No Metal device is available.");
        }

        MTLCommandQueue queue = device.NewCommandQueue();
        if (queue.NativePtr == 0)
        {
            device.Dispose();
            throw new InvalidOperationException("Could not create a Metal command queue.");
        }
        return new MetalSilkGraphicsDevice(device, queue);
    }

    /// <inheritdoc/>
    public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfZero(size);
        RegisterDependentObject();
        // Storage and Upload together are legitimate on Metal, unlike the
        // device-local/staging split D3D12 and Vulkan draw. A shared-mode buffer
        // is both CPU-writable and usable as a shader storage buffer, which is
        // exactly what the per-mesh instance table needs, so the mode selection
        // below already covers the combination.
        MTLResourceOptions options = usage.HasFlag(SilkBufferUsage.Upload)
            ? MTLResourceOptions.ResourceStorageModeShared
            : MTLResourceOptions.ResourceStorageModePrivate;
        MTLBuffer buffer = default;
        bool success = false;
        try
        {
            buffer = _device.NewBuffer(checked((ulong)size), options);
            if (buffer.NativePtr == 0)
            {
                throw new InvalidOperationException("Could not create a Metal buffer.");
            }
            success = true;
            return new MetalSilkGraphicsBuffer(this, buffer, size, usage);
        }
        finally
        {
            if (!success)
            {
                if (buffer.NativePtr != 0)
                {
                    buffer.Dispose();
                }
                ReleaseDependentObject();
            }
        }
    }

    /// <inheritdoc/>
    public void WaitIdle()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        MTLCommandBuffer commandBuffer = _queue.CommandBuffer();
        if (commandBuffer.NativePtr == 0)
        {
            throw new InvalidOperationException("Could not create a Metal command buffer.");
        }
        commandBuffer.Commit();
        commandBuffer.WaitUntilCompleted();
        commandBuffer.Dispose();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!TryBeginDispose())
        {
            return;
        }
        bool idle = false;
        try
        {
            WaitIdle();
            idle = true;
            _materialDescriptorTables?.Dispose();
            _queue.Dispose();
            _device.Dispose();
            _disposed = true;
        }
        finally
        {
            if (idle)
            {
                CompleteLifetimeDispose();
            }
            else
            {
                CancelLifetimeDispose();
            }
        }
    }

    internal void RegisterDependentObject() => RegisterDependentLifetime();

    internal void ReleaseDependentObject() => ReleaseDependentLifetime();

    private bool TryBeginDispose() => TryBeginLifetimeDispose(
        "Cannot dispose the Metal device while buffers, textures, pipelines, " +
        "pick readbacks, selection resources, submissions, or samplers are alive.");
}

[SupportedOSPlatform("macos")]
internal sealed unsafe class MetalSilkGraphicsBuffer : SilkGraphicsBufferBase
{
    private readonly MetalSilkGraphicsDevice _device;
    private MTLBuffer _buffer;

    internal MetalSilkGraphicsBuffer(
        MetalSilkGraphicsDevice device,
        MTLBuffer buffer,
        nuint size,
        SilkBufferUsage usage)
        : base(size, usage)
    {
        _device = device;
        _buffer = buffer;
    }

    public override void Write(ReadOnlySpan<byte> data, nuint offset = 0)
    {
        ThrowIfBufferDisposed();
        nuint length = ValidateWrite(data.Length, offset);
        if (length == 0)
        {
            return;
        }

        nint contents = _buffer.Contents;
        if (contents == 0)
        {
            throw new InvalidOperationException("The Metal buffer is not CPU-visible.");
        }
        data.CopyTo(new Span<byte>(
            (byte*)contents + checked((nint)offset),
            data.Length));
    }

    public override void ReadbackForTesting(Span<byte> destination)
    {
        _ = ValidateReadback(destination.Length);
        _device.Readback(this, destination);
    }

    protected override void ReleaseNative()
    {
        _buffer.Dispose();
        _device.ReleaseDependentObject();
    }

    internal MTLBuffer Buffer => _buffer;

    internal MetalSilkGraphicsDevice Device => _device;

    internal IDisposable AcquireLease() => AcquireBufferLease();

    internal void ThrowIfDisposed() => ThrowIfBufferDisposed();
}
