// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using Silk.NET.Direct3D12;

namespace OpenUsd.Rendering.Silk.D3D12;

[SupportedOSPlatform("windows")]
internal sealed unsafe class D3D12DescriptorIndexedTextureTables : IDisposable
{
    private const uint ResourceDescriptorCapacity = 4096;
    private const uint SamplerDescriptorCapacity = 2048;

    private readonly ID3D12Device* _device;
    private readonly object _gate = new();
    private readonly uint _resourceIncrement;
    private readonly uint _samplerIncrement;
    private ID3D12DescriptorHeap* _resourceHeap;
    private ID3D12DescriptorHeap* _samplerHeap;
    private uint _nextResourceDescriptor;
    private uint _nextSamplerDescriptor;

    private D3D12DescriptorIndexedTextureTables(
        ID3D12Device* device,
        ID3D12DescriptorHeap* resourceHeap,
        ID3D12DescriptorHeap* samplerHeap)
    {
        _device = device;
        _resourceHeap = resourceHeap;
        _samplerHeap = samplerHeap;
        _resourceIncrement = device->GetDescriptorHandleIncrementSize(
            DescriptorHeapType.CbvSrvUav);
        _samplerIncrement = device->GetDescriptorHandleIncrementSize(
            DescriptorHeapType.Sampler);
    }

    internal static D3D12DescriptorIndexedTextureTables? TryCreate(
        ID3D12Device* device)
    {
        ID3D12DescriptorHeap* resourceHeap = null;
        ID3D12DescriptorHeap* samplerHeap = null;
        try
        {
            resourceHeap = CreateHeap(
                device,
                DescriptorHeapType.CbvSrvUav,
                ResourceDescriptorCapacity);
            samplerHeap = CreateHeap(
                device,
                DescriptorHeapType.Sampler,
                SamplerDescriptorCapacity);
            return new D3D12DescriptorIndexedTextureTables(
                device,
                resourceHeap,
                samplerHeap);
        }
        catch
        {
            D3D12SilkGraphicsDevice.Release(ref samplerHeap);
            D3D12SilkGraphicsDevice.Release(ref resourceHeap);
            return null;
        }
    }

    internal bool TryCopySampledTexture(
        CpuDescriptorHandle source,
        out GpuDescriptorHandle handle)
    {
        lock (_gate)
        {
            if (_resourceHeap == null ||
                _nextResourceDescriptor >= ResourceDescriptorCapacity)
            {
                handle = default;
                return false;
            }
            uint descriptorIndex = _nextResourceDescriptor++;
            CpuDescriptorHandle destination = new(
                _resourceHeap->GetCPUDescriptorHandleForHeapStart().Ptr +
                (descriptorIndex * _resourceIncrement));
            _device->CopyDescriptorsSimple(
                1,
                destination,
                source,
                DescriptorHeapType.CbvSrvUav);
            handle = new GpuDescriptorHandle(
                _resourceHeap->GetGPUDescriptorHandleForHeapStart().Ptr +
                (descriptorIndex * _resourceIncrement));
            return true;
        }
    }

    internal bool TryCopySampler(
        CpuDescriptorHandle source,
        out GpuDescriptorHandle handle)
    {
        lock (_gate)
        {
            if (_samplerHeap == null ||
                _nextSamplerDescriptor >= SamplerDescriptorCapacity)
            {
                handle = default;
                return false;
            }
            uint descriptorIndex = _nextSamplerDescriptor++;
            CpuDescriptorHandle destination = new(
                _samplerHeap->GetCPUDescriptorHandleForHeapStart().Ptr +
                (descriptorIndex * _samplerIncrement));
            _device->CopyDescriptorsSimple(
                1,
                destination,
                source,
                DescriptorHeapType.Sampler);
            handle = new GpuDescriptorHandle(
                _samplerHeap->GetGPUDescriptorHandleForHeapStart().Ptr +
                (descriptorIndex * _samplerIncrement));
            return true;
        }
    }

    internal uint FillDescriptorHeaps(ID3D12DescriptorHeap** descriptorHeaps)
    {
        uint heapCount = 0;
        if (_resourceHeap != null)
        {
            descriptorHeaps[heapCount++] = _resourceHeap;
        }
        if (_samplerHeap != null)
        {
            descriptorHeaps[heapCount++] = _samplerHeap;
        }
        return heapCount;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            D3D12SilkGraphicsDevice.Release(ref _samplerHeap);
            D3D12SilkGraphicsDevice.Release(ref _resourceHeap);
        }
    }

    private static ID3D12DescriptorHeap* CreateHeap(
        ID3D12Device* device,
        DescriptorHeapType type,
        uint descriptorCount)
    {
        var description = new DescriptorHeapDesc(
            type,
            descriptorCount,
            DescriptorHeapFlags.ShaderVisible,
            0);
        Guid heapId = ID3D12DescriptorHeap.Guid;
        ID3D12DescriptorHeap* heap = null;
        global::Silk.NET.Core.Native.SilkMarshal.ThrowHResult(
            device->CreateDescriptorHeap(
                &description,
                &heapId,
                (void**)&heap));
        return heap;
    }
}
