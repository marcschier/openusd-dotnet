// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using D3D11Api = Silk.NET.Direct3D11.D3D11;

namespace OpenUsd.Rendering.Silk.Vulkan;

[SupportedOSPlatform("windows")]
internal sealed unsafe class VulkanD3D11Bridge : IDisposable
{
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);

    private readonly D3D11Api _api;
    private readonly DXGI _dxgi;
    private IDXGIFactory4* _factory;
    private IDXGIAdapter1* _adapter;
    private ID3D11Device* _device;
    private bool _disposed;

    private VulkanD3D11Bridge(
        D3D11Api api,
        DXGI dxgi,
        IDXGIFactory4* factory,
        IDXGIAdapter1* adapter,
        ID3D11Device* device)
    {
        _api = api;
        _dxgi = dxgi;
        _factory = factory;
        _adapter = adapter;
        _device = device;
    }

    internal static byte[] GetDefaultAdapterLuid()
    {
        using var dxgi = new DXGI(SilkNativeLibraryContext.Load("dxgi.dll"));
        IDXGIFactory4* factory = null;
        IDXGIAdapter1* adapter = null;
        try
        {
            Guid factoryId = IDXGIFactory4.Guid;
            SilkMarshal.ThrowHResult(
                dxgi.CreateDXGIFactory2(0, &factoryId, (void**)&factory));
            SilkMarshal.ThrowHResult(factory->EnumAdapters1(0, &adapter));
            AdapterDesc1 description;
            SilkMarshal.ThrowHResult(adapter->GetDesc1(&description));
            byte[] result = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(
                result,
                description.AdapterLuid.Low);
            BinaryPrimitives.WriteInt32LittleEndian(
                result.AsSpan(4),
                description.AdapterLuid.High);
            return result;
        }
        finally
        {
            Release(ref adapter);
            Release(ref factory);
        }
    }

    internal static VulkanD3D11Bridge Create(ReadOnlySpan<byte> adapterLuid)
    {
        if (adapterLuid.Length != 8)
        {
            throw new PlatformNotSupportedException(
                "The D3D11 Vulkan composition bridge requires an 8-byte DXGI adapter LUID.");
        }

        var api = new D3D11Api(SilkNativeLibraryContext.Load("d3d11.dll"));
        var dxgi = new DXGI(SilkNativeLibraryContext.Load("dxgi.dll"));
        IDXGIFactory4* factory = null;
        IDXGIAdapter1* adapter = null;
        ID3D11Device* device = null;
        try
        {
            Guid factoryId = IDXGIFactory4.Guid;
            SilkMarshal.ThrowHResult(
                dxgi.CreateDXGIFactory2(0, &factoryId, (void**)&factory));

            Span<byte> candidateLuid = stackalloc byte[8];
            for (uint index = 0; ; index++)
            {
                IDXGIAdapter1* candidate = null;
                int result = factory->EnumAdapters1(index, &candidate);
                if (result == DxgiErrorNotFound)
                {
                    break;
                }
                SilkMarshal.ThrowHResult(result);
                AdapterDesc1 description;
                SilkMarshal.ThrowHResult(candidate->GetDesc1(&description));
                BinaryPrimitives.WriteUInt32LittleEndian(
                    candidateLuid,
                    description.AdapterLuid.Low);
                BinaryPrimitives.WriteInt32LittleEndian(
                    candidateLuid[4..],
                    description.AdapterLuid.High);
                if (candidateLuid.SequenceEqual(adapterLuid))
                {
                    adapter = candidate;
                    break;
                }
                Release(ref candidate);
            }

            if (adapter == null)
            {
                throw new PlatformNotSupportedException(
                    "No DXGI adapter matches the Vulkan/compositor adapter LUID.");
            }

            ID3D11DeviceContext* context = null;
            D3DFeatureLevel selectedFeatureLevel;
            SilkMarshal.ThrowHResult(api.CreateDevice(
                (IDXGIAdapter*)adapter,
                D3DDriverType.Unknown,
                0,
                (uint)CreateDeviceFlag.BgraSupport,
                null,
                0,
                D3D11Api.SdkVersion,
                &device,
                &selectedFeatureLevel,
                &context));
            _ = selectedFeatureLevel;
            Release(ref context);
            return new VulkanD3D11Bridge(api, dxgi, factory, adapter, device);
        }
        catch
        {
            Release(ref device);
            Release(ref adapter);
            Release(ref factory);
            api.Dispose();
            dxgi.Dispose();
            throw;
        }
    }

    internal VulkanD3D11SharedTexture CreateSharedTexture(ViewportDimensions size) =>
        VulkanD3D11SharedTexture.Create(_device, size);

    internal void ReadbackSharedTexture(
        nint handle,
        uint acquireKey,
        uint releaseKey,
        Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ID3D11Device* device = null;
        ID3D11Device1* device1 = null;
        ID3D11DeviceContext* context = null;
        ID3D11Texture2D* sharedTexture = null;
        ID3D11Texture2D* stagingTexture = null;
        IDXGIKeyedMutex* keyedMutex = null;
        bool mutexAcquired = false;
        bool mapped = false;
        try
        {
            D3DFeatureLevel selectedFeatureLevel;
            SilkMarshal.ThrowHResult(_api.CreateDevice(
                (IDXGIAdapter*)_adapter,
                D3DDriverType.Unknown,
                0,
                (uint)CreateDeviceFlag.BgraSupport,
                null,
                0,
                D3D11Api.SdkVersion,
                &device,
                &selectedFeatureLevel,
                &context));
            _ = selectedFeatureLevel;

            Guid device1Id = ID3D11Device1.Guid;
            SilkMarshal.ThrowHResult(((IUnknown*)device)->QueryInterface(
                &device1Id,
                (void**)&device1));
            Guid textureId = ID3D11Texture2D.Guid;
            SilkMarshal.ThrowHResult(device1->OpenSharedResource1(
                (void*)handle,
                &textureId,
                (void**)&sharedTexture));
            Guid keyedMutexId = IDXGIKeyedMutex.Guid;
            SilkMarshal.ThrowHResult(((IUnknown*)sharedTexture)->QueryInterface(
                &keyedMutexId,
                (void**)&keyedMutex));
            int acquireResult = keyedMutex->AcquireSync(acquireKey, uint.MaxValue);
            if (acquireResult != 0)
            {
                throw new InvalidOperationException(
                    $"The second D3D11 device could not acquire key {acquireKey}: " +
                    $"0x{acquireResult:X8}.");
            }
            mutexAcquired = true;

            Texture2DDesc description;
            sharedTexture->GetDesc(&description);
            int rowBytes = checked((int)description.Width * 4);
            int requiredLength = checked(rowBytes * (int)description.Height);
            if (destination.Length != requiredLength)
            {
                throw new ArgumentException(
                    $"The destination must contain exactly {requiredLength} bytes.",
                    nameof(destination));
            }
            description.Usage = Usage.Staging;
            description.BindFlags = 0;
            description.CPUAccessFlags = (uint)CpuAccessFlag.Read;
            description.MiscFlags = 0;
            SilkMarshal.ThrowHResult(device->CreateTexture2D(
                &description,
                null,
                &stagingTexture));
            context->CopyResource(
                (ID3D11Resource*)stagingTexture,
                (ID3D11Resource*)sharedTexture);
            MappedSubresource mapping;
            SilkMarshal.ThrowHResult(context->Map(
                (ID3D11Resource*)stagingTexture,
                0,
                Map.Read,
                0,
                &mapping));
            mapped = true;
            fixed (byte* destinationPointer = destination)
            {
                for (uint row = 0; row < description.Height; row++)
                {
                    byte* sourceRow = (byte*)mapping.PData + (row * mapping.RowPitch);
                    byte* destinationRow = destinationPointer + (row * rowBytes);
                    Buffer.MemoryCopy(sourceRow, destinationRow, rowBytes, rowBytes);
                }
            }
        }
        finally
        {
            if (mapped)
            {
                context->Unmap((ID3D11Resource*)stagingTexture, 0);
            }
            if (mutexAcquired)
            {
                _ = keyedMutex->ReleaseSync(releaseKey);
            }
            Release(ref keyedMutex);
            Release(ref stagingTexture);
            Release(ref sharedTexture);
            Release(ref device1);
            Release(ref context);
            Release(ref device);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        Release(ref _device);
        Release(ref _adapter);
        Release(ref _factory);
        _api.Dispose();
        _dxgi.Dispose();
        _disposed = true;
    }

    internal static void Release<T>(ref T* value)
        where T : unmanaged
    {
        if (value == null)
        {
            return;
        }
        _ = ((IUnknown*)value)->Release();
        value = null;
    }
}

[SupportedOSPlatform("windows")]
internal sealed unsafe class VulkanD3D11SharedTexture : IDisposable
{
    private const uint SharedResourceReadWrite = 0x80000001;

    private ID3D11Texture2D* _texture;
    private IDXGIKeyedMutex* _keyedMutex;
    private SafeHandle? _handle;
    private bool _disposed;

    private VulkanD3D11SharedTexture()
    {
    }

    internal SafeHandle Handle =>
        _handle ?? throw new ObjectDisposedException(nameof(VulkanD3D11SharedTexture));

    internal static VulkanD3D11SharedTexture Create(
        ID3D11Device* device,
        ViewportDimensions size)
    {
        var result = new VulkanD3D11SharedTexture();
        try
        {
            var description = new Texture2DDesc
            {
                Width = checked((uint)size.Width),
                Height = checked((uint)size.Height),
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.FormatR8G8B8A8Unorm,
                SampleDesc = new SampleDesc(1, 0),
                Usage = Usage.Default,
                BindFlags = (uint)(BindFlag.RenderTarget | BindFlag.ShaderResource),
                MiscFlags = (uint)(ResourceMiscFlag.SharedKeyedmutex |
                    ResourceMiscFlag.SharedNthandle)
            };
            ID3D11Texture2D* texture = null;
            SilkMarshal.ThrowHResult(device->CreateTexture2D(
                &description,
                null,
                &texture));
            result._texture = texture;

            Guid keyedMutexId = IDXGIKeyedMutex.Guid;
            IDXGIKeyedMutex* keyedMutex = null;
            SilkMarshal.ThrowHResult(((IUnknown*)texture)->QueryInterface(
                &keyedMutexId,
                (void**)&keyedMutex));
            result._keyedMutex = keyedMutex;

            IDXGIResource1* resource = null;
            try
            {
                Guid resourceId = IDXGIResource1.Guid;
                SilkMarshal.ThrowHResult(((IUnknown*)texture)->QueryInterface(
                    &resourceId,
                    (void**)&resource));
                void* handle = null;
                SilkMarshal.ThrowHResult(resource->CreateSharedHandle(
                    null,
                    SharedResourceReadWrite,
                    (char*)null,
                    &handle));
                result._handle = new VulkanWin32SafeHandle((nint)handle);
            }
            finally
            {
                VulkanD3D11Bridge.Release(ref resource);
            }
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    internal void CompleteConsumerRoundTrip()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SilkMarshal.ThrowHResult(_keyedMutex->AcquireSync(1, uint.MaxValue));
        SilkMarshal.ThrowHResult(_keyedMutex->ReleaseSync(0));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _handle?.Dispose();
        _handle = null;
        VulkanD3D11Bridge.Release(ref _keyedMutex);
        VulkanD3D11Bridge.Release(ref _texture);
        _disposed = true;
    }
}
