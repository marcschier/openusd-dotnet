// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;
using Silk.NET.Maths;

namespace OpenUsd.Rendering.Silk.D3D12;

[SupportedOSPlatform("windows")]
public sealed unsafe partial class D3D12SilkGraphicsDevice
{
    /// <inheritdoc/>
    public ISilkGraphicsTexture CreateTexture2D(
        uint width,
        uint height,
        SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
        CreateTexture2D(new SilkTextureDescriptor(
            width,
            height,
            format,
            SilkTextureDescriptor.GetDefaultUsage(format)));

    /// <inheritdoc/>
    public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_device == null, this);
        descriptor.Validate();
        RegisterDependentObject();

        bool sampled = descriptor.Usage.HasFlag(SilkTextureUsage.Sampled);
        bool sampledDepth =
            sampled && descriptor.Format == SilkTextureFormat.D32Float;
        Format nativeFormat = descriptor.Format == SilkTextureFormat.Rgba8Unorm
            ? Format.FormatR8G8B8A8Unorm
            : sampledDepth
                ? Format.FormatR32Typeless
                : Format.FormatD32Float;
        ResourceFlags resourceFlags = ResourceFlags.None;
        if (descriptor.Usage.HasFlag(SilkTextureUsage.ColorRenderTarget))
        {
            resourceFlags |= ResourceFlags.AllowRenderTarget;
        }
        if (descriptor.Usage.HasFlag(SilkTextureUsage.DepthRenderTarget))
        {
            resourceFlags |= ResourceFlags.AllowDepthStencil;
        }
        var heapProperties = new HeapProperties(HeapType.Default);
        var description = new ResourceDesc(
            ResourceDimension.Texture2D,
            0,
            descriptor.Width,
            descriptor.Height,
            1,
            1,
            nativeFormat,
            new SampleDesc(1, 0),
            TextureLayout.LayoutUnknown,
            resourceFlags);
        bool hasAttachment =
            descriptor.Usage.HasFlag(SilkTextureUsage.ColorRenderTarget) ||
            descriptor.Usage.HasFlag(SilkTextureUsage.DepthRenderTarget);
        Format attachmentFormat = descriptor.Format == SilkTextureFormat.Rgba8Unorm
            ? Format.FormatR8G8B8A8Unorm
            : Format.FormatD32Float;
        var clearValue = new ClearValue(attachmentFormat);
        if (descriptor.Usage.HasFlag(SilkTextureUsage.DepthRenderTarget))
        {
            clearValue.DepthStencil = new DepthStencilValue(1, 0);
        }
        ID3D12Resource* resource = null;
        ID3D12DescriptorHeap* attachmentDescriptorHeap = null;
        ID3D12DescriptorHeap* shaderResourceDescriptorHeap = null;
        bool success = false;
        try
        {
            Guid resourceId = ID3D12Resource.Guid;
            ClearValue* clearValuePointer = &clearValue;
            SilkMarshal.ThrowHResult(_device->CreateCommittedResource(
                &heapProperties,
                HeapFlags.None,
                &description,
                ResourceStates.Common,
                hasAttachment ? clearValuePointer : null,
                &resourceId,
                (void**)&resource));

            CpuDescriptorHandle handle = default;
            if (hasAttachment)
            {
                uint attachmentDescriptorCount = sampledDepth ? 2u : 1u;
                var heapDescription = new DescriptorHeapDesc(
                    descriptor.Usage.HasFlag(SilkTextureUsage.ColorRenderTarget)
                        ? DescriptorHeapType.Rtv
                        : DescriptorHeapType.Dsv,
                    attachmentDescriptorCount,
                    DescriptorHeapFlags.None,
                    0);
                Guid heapId = ID3D12DescriptorHeap.Guid;
                SilkMarshal.ThrowHResult(_device->CreateDescriptorHeap(
                    &heapDescription,
                    &heapId,
                    (void**)&attachmentDescriptorHeap));
                handle =
                    attachmentDescriptorHeap->GetCPUDescriptorHandleForHeapStart();
                if (descriptor.Usage.HasFlag(SilkTextureUsage.ColorRenderTarget))
                {
                    _device->CreateRenderTargetView(resource, null, handle);
                }
                else if (sampledDepth)
                {
                    var writableView = new DepthStencilViewDesc
                    {
                        Format = Format.FormatD32Float,
                        ViewDimension = DsvDimension.Texture2D,
                        Flags = DsvFlags.None,
                        Texture2D = new Tex2DDsv(0)
                    };
                    _device->CreateDepthStencilView(resource, &writableView, handle);
                }
                else
                {
                    _device->CreateDepthStencilView(resource, null, handle);
                }
            }
            CpuDescriptorHandle shaderResourceView = default;
            CpuDescriptorHandle readOnlyDepthView = default;
            if (sampled)
            {
                var heapDescription = new DescriptorHeapDesc(
                    DescriptorHeapType.CbvSrvUav,
                    1,
                    DescriptorHeapFlags.None,
                    0);
                Guid heapId = ID3D12DescriptorHeap.Guid;
                SilkMarshal.ThrowHResult(_device->CreateDescriptorHeap(
                    &heapDescription,
                    &heapId,
                    (void**)&shaderResourceDescriptorHeap));
                shaderResourceView =
                    shaderResourceDescriptorHeap->GetCPUDescriptorHandleForHeapStart();
                var view = new ShaderResourceViewDesc
                {
                    Format = sampledDepth
                        ? Format.FormatR32Float
                        : Format.FormatR8G8B8A8Unorm,
                    ViewDimension = SrvDimension.Texture2D,
                    Shader4ComponentMapping = 0x1688,
                    Texture2D = new Tex2DSrv(0, 1, 0, 0)
                };
                _device->CreateShaderResourceView(resource, &view, shaderResourceView);

                if (sampledDepth)
                {
                    uint increment = _device->GetDescriptorHandleIncrementSize(
                        DescriptorHeapType.Dsv);
                    readOnlyDepthView = new CpuDescriptorHandle(
                        handle.Ptr + increment);
                    var readOnlyView = new DepthStencilViewDesc
                    {
                        Format = Format.FormatD32Float,
                        ViewDimension = DsvDimension.Texture2D,
                        Flags = DsvFlags.ReadOnlyDepth,
                        Texture2D = new Tex2DDsv(0)
                    };
                    _device->CreateDepthStencilView(
                        resource,
                        &readOnlyView,
                        readOnlyDepthView);
                }
            }
            success = true;
            return new D3D12SilkGraphicsTexture(
                this,
                resource,
                attachmentDescriptorHeap,
                shaderResourceDescriptorHeap,
                handle,
                readOnlyDepthView,
                shaderResourceView,
                descriptor);
        }
        finally
        {
            if (!success)
            {
                Release(ref shaderResourceDescriptorHeap);
                Release(ref attachmentDescriptorHeap);
            }
            if (!success && resource != null)
            {
                _ = resource->Release();
            }
            if (!success)
            {
                ReleaseDependentObject();
            }
        }
    }

    /// <inheritdoc/>
    public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_device == null, this);
        descriptor.Validate();
        RegisterDependentObject();
        ID3D12DescriptorHeap* heap = null;
        bool success = false;
        try
        {
            var heapDescription = new DescriptorHeapDesc(
                DescriptorHeapType.Sampler,
                1,
                DescriptorHeapFlags.ShaderVisible,
                0);
            Guid heapId = ID3D12DescriptorHeap.Guid;
            SilkMarshal.ThrowHResult(_device->CreateDescriptorHeap(
                &heapDescription,
                &heapId,
                (void**)&heap));
            var nativeDescriptor = new SamplerDesc
            {
                Filter = GetFilter(descriptor.MinFilter, descriptor.MagFilter),
                AddressU = GetAddressMode(descriptor.AddressU),
                AddressV = GetAddressMode(descriptor.AddressV),
                AddressW = GetAddressMode(descriptor.AddressW),
                ComparisonFunc = ComparisonFunc.Always,
                MinLOD = 0,
                MaxLOD = float.MaxValue
            };
            _device->CreateSampler(
                &nativeDescriptor,
                heap->GetCPUDescriptorHandleForHeapStart());
            success = true;
            return new D3D12SilkGraphicsSampler(this, heap, descriptor);
        }
        finally
        {
            if (!success)
            {
                Release(ref heap);
                ReleaseDependentObject();
            }
        }
    }

    /// <inheritdoc/>
    public ISilkGraphicsCommandList CreateCommandList()
    {
        ObjectDisposedException.ThrowIf(_device == null, this);
        return new D3D12SilkGraphicsCommandList(this);
    }

    /// <inheritdoc/>
    public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList)
    {
        ObjectDisposedException.ThrowIf(_device == null || _queue == null, this);
        ArgumentNullException.ThrowIfNull(commandList);
        if (commandList is not D3D12SilkGraphicsCommandList commands ||
            !ReferenceEquals(commands.Device, this))
        {
            throw new ArgumentException(
                "The command list was not created by this D3D12 device.",
                nameof(commandList));
        }
        commands.MarkSubmitted();

        D3D12SilkPickReadbackBuffer? pickExecution =
            commands.PickReadbackDestination;
        var leases = new List<IDisposable>();
        var uploadResources = new List<nint>();
        var materialHeaps = new List<nint>();
        ID3D12CommandAllocator* allocator = null;
        ID3D12GraphicsCommandList* nativeCommands = null;
        ID3D12Fence* completionFence = null;
        ulong completionValue = 1;
        bool borrowedPickExecution = false;
        bool dependentRegistered = false;
        bool workSubmitted = false;
        bool success = false;
        try
        {
            var leasedTextures = new HashSet<D3D12SilkGraphicsTexture>();
            var leasedPipelines = new HashSet<D3D12SilkGraphicsPipeline>();
            var leasedPickPipelines =
                new HashSet<D3D12SilkPickGraphicsPipeline>();
            var leasedPickReadbacks =
                new HashSet<D3D12SilkPickReadbackBuffer>();
            var leasedComputePipelines = new HashSet<D3D12SilkComputePipeline>();
            var leasedBuffers = new HashSet<D3D12SilkGraphicsBuffer>();
            foreach (D3D12GraphicsCommand command in commands.Commands)
            {
                if (command.Texture is { } texture && leasedTextures.Add(texture))
                {
                    leases.Add(texture.AcquireLease());
                }
                if (command.DepthTexture is { } depthTexture &&
                    leasedTextures.Add(depthTexture))
                {
                    leases.Add(depthTexture.AcquireLease());
                }
                if (command.Pipeline is { } leasedPipeline &&
                    leasedPipelines.Add(leasedPipeline))
                {
                    leases.Add(leasedPipeline.AcquireLease());
                }
                if (command.PickPipeline is { } leasedPickPipeline &&
                    leasedPickPipelines.Add(leasedPickPipeline))
                {
                    leases.Add(leasedPickPipeline.AcquireLease());
                }
                if (command.PickReadback is { } leasedPickReadback &&
                    leasedPickReadbacks.Add(leasedPickReadback))
                {
                    leases.Add(leasedPickReadback.AcquireLease());
                }
                if (command.SelectionMaskPipeline is { } leasedSelectionMaskPipeline)
                {
                    leases.Add(leasedSelectionMaskPipeline.AcquireLease());
                }
                if (command.SelectionOutlinePipeline is
                    { } leasedSelectionOutlinePipeline)
                {
                    leases.Add(leasedSelectionOutlinePipeline.AcquireLease());
                }
                if (command.SelectionOutlineBinding is
                    { } leasedSelectionOutlineBinding)
                {
                    leases.Add(leasedSelectionOutlineBinding.AcquireLease());
                }
                if (command.ComputePipeline is { } leasedComputePipeline &&
                    leasedComputePipelines.Add(leasedComputePipeline))
                {
                    leases.Add(leasedComputePipeline.AcquireLease());
                }
                if (command.Buffer is { } buffer && leasedBuffers.Add(buffer))
                {
                    leases.Add(buffer.AcquireLease());
                }
            }
            RegisterDependentObject();
            dependentRegistered = true;

            if (pickExecution is null)
            {
                Guid allocatorId = ID3D12CommandAllocator.Guid;
                SilkMarshal.ThrowHResult(_device->CreateCommandAllocator(
                    CommandListType.Direct,
                    &allocatorId,
                    (void**)&allocator));
                Guid commandListId = ID3D12GraphicsCommandList.Guid;
                SilkMarshal.ThrowHResult(_device->CreateCommandList(
                    0,
                    CommandListType.Direct,
                    allocator,
                    null,
                    &commandListId,
                    (void**)&nativeCommands));
            }
            else
            {
                D3D12PickExecution execution = pickExecution.BeginSubmission();
                allocator = execution.Allocator;
                nativeCommands = execution.Commands;
                completionFence = execution.Fence;
                completionValue = execution.FenceValue;
                borrowedPickExecution = true;
            }

            var finalStates = new Dictionary<D3D12SilkGraphicsTexture, ResourceStates>();
            var finalBufferStates =
                new Dictionary<D3D12SilkGraphicsBuffer, ResourceStates>();
            var pendingUavWrites = new HashSet<D3D12SilkGraphicsBuffer>();
            D3D12SilkGraphicsTexture? colorAttachment = null;
            D3D12SilkGraphicsTexture? depthAttachment = null;
            D3D12SilkGraphicsPipeline? pipeline = null;
            D3D12SilkPickGraphicsPipeline? pickPipeline = null;
            D3D12SilkSelectionMaskGraphicsPipeline? selectionMaskPipeline = null;
            D3D12SilkSelectionOutlineGraphicsPipeline? selectionOutlinePipeline =
                null;
            D3D12SilkSelectionOutlineBinding? selectionOutlineBinding = null;
            D3D12SelectionRenderingKind selectionRenderingKind =
                D3D12SelectionRenderingKind.None;
            uint pickBaseToken = 0;
            D3D12SilkGraphicsBuffer? vertexBuffer = null;
            D3D12SilkGraphicsBuffer? indexBuffer = null;
            D3D12SilkGraphicsBuffer? uniformBuffer = null;
            D3D12SilkComputePipeline? computePipeline = null;
            D3D12SilkGraphicsBuffer? storageBuffer = null;
            D3D12SilkGraphicsBuffer? computeUniformBuffer = null;
            List<D3D12MaterialBinding> materialBindings = [];
            D3D12SilkGraphicsTexture? materialTexture;
            SilkViewport? currentViewport = null;
            SilkScissor? currentScissor = null;
            bool rendering = false;
            float* color = stackalloc float[4];
            ID3D12DescriptorHeap** descriptorHeaps =
                stackalloc ID3D12DescriptorHeap*[2];
            foreach (D3D12GraphicsCommand command in commands.Commands)
            {
                switch (command.SelectionOutlineKind)
                {
                    case D3D12SelectionOutlineCommandKind.BeginMask:
                        colorAttachment = command.Texture!;
                        depthAttachment = command.DepthTexture!;
                        colorAttachment.ThrowIfDisposed();
                        depthAttachment.ThrowIfDisposed();
                        Transition(
                            nativeCommands,
                            colorAttachment.Resource,
                            GetCurrentState(finalStates, colorAttachment),
                            ResourceStates.RenderTarget);
                        Transition(
                            nativeCommands,
                            depthAttachment.Resource,
                            GetCurrentState(finalStates, depthAttachment),
                            ResourceStates.DepthRead);
                        CpuDescriptorHandle maskView =
                            colorAttachment.AttachmentView;
                        CpuDescriptorHandle readOnlyDepthView =
                            depthAttachment.ReadOnlyDepthView;
                        nativeCommands->OMSetRenderTargets(
                            1,
                            &maskView,
                            false,
                            &readOnlyDepthView);
                        finalStates[colorAttachment] = ResourceStates.RenderTarget;
                        finalStates[depthAttachment] = ResourceStates.DepthRead;
                        selectionRenderingKind = D3D12SelectionRenderingKind.Mask;
                        rendering = true;
                        continue;
                    case D3D12SelectionOutlineCommandKind.SetMaskPipeline:
                        selectionMaskPipeline = command.SelectionMaskPipeline!;
                        selectionMaskPipeline.ThrowIfDisposed();
                        if (selectionMaskPipeline.DeviceGeneration !=
                            SelectionOutlineDeviceGeneration)
                        {
                            throw new InvalidOperationException(
                                "The ordered D3D12 selection-mask pipeline generation is no longer current.");
                        }
                        pipeline = null;
                        pickPipeline = null;
                        selectionOutlinePipeline = null;
                        selectionOutlineBinding = null;
                        nativeCommands->SetGraphicsRootSignature(
                            selectionMaskPipeline.RootSignature);
                        nativeCommands->SetPipelineState(
                            selectionMaskPipeline.Pipeline);
                        continue;
                    case D3D12SelectionOutlineCommandKind.BeginOutline:
                        colorAttachment = command.Texture!;
                        depthAttachment = null;
                        colorAttachment.ThrowIfDisposed();
                        Transition(
                            nativeCommands,
                            colorAttachment.Resource,
                            GetCurrentState(finalStates, colorAttachment),
                            ResourceStates.RenderTarget);
                        CpuDescriptorHandle outlineView =
                            colorAttachment.AttachmentView;
                        nativeCommands->OMSetRenderTargets(
                            1,
                            &outlineView,
                            false,
                            null);
                        finalStates[colorAttachment] = ResourceStates.RenderTarget;
                        selectionRenderingKind =
                            D3D12SelectionRenderingKind.Outline;
                        rendering = true;
                        continue;
                    case D3D12SelectionOutlineCommandKind.SetOutlinePipeline:
                        selectionOutlinePipeline =
                            command.SelectionOutlinePipeline!;
                        selectionOutlinePipeline.ThrowIfDisposed();
                        if (selectionOutlinePipeline.DeviceGeneration !=
                            SelectionOutlineDeviceGeneration)
                        {
                            throw new InvalidOperationException(
                                "The ordered D3D12 selection-outline pipeline generation is no longer current.");
                        }
                        pipeline = null;
                        pickPipeline = null;
                        selectionMaskPipeline = null;
                        nativeCommands->SetGraphicsRootSignature(
                            selectionOutlinePipeline.RootSignature);
                        nativeCommands->SetPipelineState(
                            selectionOutlinePipeline.Pipeline);
                        continue;
                    case D3D12SelectionOutlineCommandKind.SetBinding:
                        selectionOutlineBinding =
                            command.SelectionOutlineBinding!;
                        selectionOutlineBinding.ThrowIfDisposed();
                        if (selectionOutlineBinding.DeviceGeneration !=
                            SelectionOutlineDeviceGeneration)
                        {
                            throw new InvalidOperationException(
                                "The ordered D3D12 selection-outline binding generation is no longer current.");
                        }
                        descriptorHeaps[0] =
                            selectionOutlineBinding.ResourceHeap;
                        descriptorHeaps[1] =
                            selectionOutlineBinding.SamplerHeap;
                        nativeCommands->SetDescriptorHeaps(2, descriptorHeaps);
                        nativeCommands->SetGraphicsRootDescriptorTable(
                            0,
                            selectionOutlineBinding.ResourceHeap
                                ->GetGPUDescriptorHandleForHeapStart());
                        nativeCommands->SetGraphicsRootDescriptorTable(
                            1,
                            selectionOutlineBinding.SamplerHeap
                                ->GetGPUDescriptorHandleForHeapStart());
                        nativeCommands->SetGraphicsRootConstantBufferView(
                            2,
                            selectionOutlineBinding.Parameters.Resource
                                ->GetGPUVirtualAddress());
                        continue;
                    case D3D12SelectionOutlineCommandKind.DrawFullscreenTriangle:
                        if (!rendering ||
                            selectionRenderingKind !=
                                D3D12SelectionRenderingKind.Outline ||
                            colorAttachment is null ||
                            selectionOutlinePipeline is null ||
                            selectionOutlineBinding is null ||
                            currentViewport is null ||
                            currentScissor is null)
                        {
                            throw new InvalidOperationException(
                                "The ordered D3D12 selection-outline draw state is incomplete.");
                        }
                        nativeCommands->IASetPrimitiveTopology(
                            D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
                        nativeCommands->DrawInstanced(3, 1, 0, 0);
                        continue;
                    case D3D12SelectionOutlineCommandKind.None:
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Unknown D3D12 selection-outline command.");
                }

                switch (command.PickKind)
                {
                    case D3D12PickCommandKind.SetPipeline:
                        pickPipeline = command.PickPipeline!;
                        pickPipeline.ThrowIfDisposed();
                        if (pickPipeline.DeviceGeneration != PickDeviceGeneration)
                        {
                            throw new InvalidOperationException(
                                "The ordered D3D12 pick pipeline generation is no longer current.");
                        }
                        pipeline = null;
                        selectionMaskPipeline = null;
                        selectionOutlinePipeline = null;
                        selectionOutlineBinding = null;
                        pickBaseToken = 0;
                        nativeCommands->SetGraphicsRootSignature(
                            pickPipeline.RootSignature);
                        nativeCommands->SetPipelineState(pickPipeline.Pipeline);
                        continue;
                    case D3D12PickCommandKind.SetBaseToken:
                        if (pickPipeline is null || command.PickBaseToken == 0)
                        {
                            throw new InvalidOperationException(
                                "The ordered D3D12 pick token has no active pick pipeline.");
                        }
                        pickBaseToken = command.PickBaseToken;
                        SetPickRootConstants(nativeCommands, pickBaseToken);
                        continue;
                    case D3D12PickCommandKind.CopyPixel:
                        D3D12SilkGraphicsTexture pickSource = command.Texture!;
                        D3D12SilkPickReadbackBuffer pickDestination =
                            command.PickReadback!;
                        pickSource.ThrowIfDisposed();
                        pickDestination.ThrowIfDisposed();
                        if (pickDestination.DeviceGeneration != PickDeviceGeneration)
                        {
                            throw new InvalidOperationException(
                                "The ordered D3D12 pick readback generation is no longer current.");
                        }
                        ThrowIfInjectedPickCopyFailure();
                        ResourceStates pickPreviousState =
                            GetCurrentState(finalStates, pickSource);
                        Transition(
                            nativeCommands,
                            pickSource.Resource,
                            pickPreviousState,
                            ResourceStates.CopySource);
                        var pickDestinationLocation = new TextureCopyLocation(
                            pickDestination.Resource,
                            TextureCopyType.PlacedFootprint)
                        {
                            PlacedFootprint = new PlacedSubresourceFootprint(
                                0,
                                new SubresourceFootprint(
                                    Format.FormatR8G8B8A8Unorm,
                                    1,
                                    1,
                                    1,
                                    PickReadbackRowPitch))
                        };
                        var pickSourceLocation = new TextureCopyLocation(
                            pickSource.Resource,
                            TextureCopyType.SubresourceIndex)
                        {
                            SubresourceIndex = 0
                        };
                        var sourceBox = new Box(
                            command.PickCoordinate.X,
                            command.PickCoordinate.Y,
                            0,
                            checked(command.PickCoordinate.X + 1),
                            checked(command.PickCoordinate.Y + 1),
                            1);
                        nativeCommands->CopyTextureRegion(
                            &pickDestinationLocation,
                            0,
                            0,
                            0,
                            &pickSourceLocation,
                            &sourceBox);
                        Transition(
                            nativeCommands,
                            pickSource.Resource,
                            ResourceStates.CopySource,
                            pickPreviousState);
                        finalStates[pickSource] = pickPreviousState;
                        RecordPickCopy(command.PickCoordinate);
                        continue;
                    case D3D12PickCommandKind.None:
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Unknown D3D12 pick command.");
                }

                switch (command.Kind)
                {
                    case SilkGraphicsCommandKind.UploadTexture:
                        D3D12SilkGraphicsTexture uploadTexture = command.Texture!;
                        uploadTexture.ThrowIfDisposed();
                        ResourceStates uploadPreviousState =
                            GetCurrentState(finalStates, uploadTexture);
                        CreateTextureUpload(
                            uploadTexture,
                            command.Data!,
                            out ID3D12Resource* upload,
                            out PlacedSubresourceFootprint footprint);
                        uploadResources.Add((nint)upload);
                        Transition(
                            nativeCommands,
                            uploadTexture.Resource,
                            uploadPreviousState,
                            ResourceStates.CopyDest);
                        var sourceLocation = new TextureCopyLocation(
                            upload,
                            TextureCopyType.PlacedFootprint)
                        {
                            PlacedFootprint = footprint
                        };
                        var destinationLocation = new TextureCopyLocation(
                            uploadTexture.Resource,
                            TextureCopyType.SubresourceIndex)
                        {
                            SubresourceIndex = 0
                        };
                        nativeCommands->CopyTextureRegion(
                            &destinationLocation,
                            0,
                            0,
                            0,
                            &sourceLocation,
                            null);
                        Transition(
                            nativeCommands,
                            uploadTexture.Resource,
                            ResourceStates.CopyDest,
                            ResourceStates.CopySource);
                        finalStates[uploadTexture] = ResourceStates.CopySource;
                        break;
                    case SilkGraphicsCommandKind.ClearColor:
                        D3D12SilkGraphicsTexture colorTexture = command.Texture!;
                        colorTexture.ThrowIfDisposed();
                        ResourceStates colorPreviousState =
                            GetCurrentState(finalStates, colorTexture);
                        Transition(
                            nativeCommands,
                            colorTexture.Resource,
                            colorPreviousState,
                            ResourceStates.RenderTarget);
                        color[0] = command.Color.Red;
                        color[1] = command.Color.Green;
                        color[2] = command.Color.Blue;
                        color[3] = command.Color.Alpha;
                        nativeCommands->ClearRenderTargetView(
                            colorTexture.AttachmentView,
                            color,
                            0,
                            null);
                        Transition(
                            nativeCommands,
                            colorTexture.Resource,
                            ResourceStates.RenderTarget,
                            ResourceStates.CopySource);
                        finalStates[colorTexture] = ResourceStates.CopySource;
                        break;
                    case SilkGraphicsCommandKind.ClearDepth:
                        D3D12SilkGraphicsTexture depthTextureToClear = command.Texture!;
                        depthTextureToClear.ThrowIfDisposed();
                        ResourceStates depthPreviousState =
                            GetCurrentState(finalStates, depthTextureToClear);
                        Transition(
                            nativeCommands,
                            depthTextureToClear.Resource,
                            depthPreviousState,
                            ResourceStates.DepthWrite);
                        nativeCommands->ClearDepthStencilView(
                            depthTextureToClear.AttachmentView,
                            ClearFlags.Depth,
                            command.Depth,
                            0,
                            0,
                            null);
                        Transition(
                            nativeCommands,
                            depthTextureToClear.Resource,
                            ResourceStates.DepthWrite,
                            ResourceStates.CopySource);
                        finalStates[depthTextureToClear] = ResourceStates.CopySource;
                        break;
                    case SilkGraphicsCommandKind.BeginRendering:
                        colorAttachment = command.Texture!;
                        depthAttachment = command.DepthTexture!;
                        colorAttachment.ThrowIfDisposed();
                        depthAttachment.ThrowIfDisposed();
                        Transition(
                            nativeCommands,
                            colorAttachment.Resource,
                            GetCurrentState(finalStates, colorAttachment),
                            ResourceStates.RenderTarget);
                        Transition(
                            nativeCommands,
                            depthAttachment.Resource,
                            GetCurrentState(finalStates, depthAttachment),
                            ResourceStates.DepthWrite);
                        CpuDescriptorHandle colorView = colorAttachment.AttachmentView;
                        CpuDescriptorHandle depthView = depthAttachment.AttachmentView;
                        nativeCommands->OMSetRenderTargets(
                            1,
                            &colorView,
                            false,
                            &depthView);
                        finalStates[colorAttachment] = ResourceStates.RenderTarget;
                        finalStates[depthAttachment] = ResourceStates.DepthWrite;
                        selectionRenderingKind = D3D12SelectionRenderingKind.None;
                        rendering = true;
                        break;
                    case SilkGraphicsCommandKind.SetGraphicsPipeline:
                        pipeline = command.Pipeline!;
                        pipeline.ThrowIfDisposed();
                        pickPipeline = null;
                        pickBaseToken = 0;
                        selectionMaskPipeline = null;
                        selectionOutlinePipeline = null;
                        selectionOutlineBinding = null;
                        nativeCommands->SetGraphicsRootSignature(pipeline.RootSignature);
                        nativeCommands->SetPipelineState(pipeline.Pipeline);
                        break;
                    case SilkGraphicsCommandKind.SetViewport:
                        currentViewport = command.Viewport;
                        var viewport = new Viewport(
                            command.Viewport.X,
                            command.Viewport.Y,
                            command.Viewport.Width,
                            command.Viewport.Height,
                            command.Viewport.MinDepth,
                            command.Viewport.MaxDepth);
                        nativeCommands->RSSetViewports(1, &viewport);
                        break;
                    case SilkGraphicsCommandKind.SetScissor:
                        currentScissor = command.Scissor;
                        var scissor = new Box2D<int>(
                            command.Scissor.X,
                            command.Scissor.Y,
                            checked(command.Scissor.X + (int)command.Scissor.Width),
                            checked(command.Scissor.Y + (int)command.Scissor.Height));
                        nativeCommands->RSSetScissorRects(1, &scissor);
                        break;
                    case SilkGraphicsCommandKind.SetVertexBuffer:
                        vertexBuffer = command.Buffer!;
                        vertexBuffer.ThrowIfDisposed();
                        PrepareBufferState(
                            nativeCommands,
                            finalBufferStates,
                            pendingUavWrites,
                            vertexBuffer,
                            ResourceStates.VertexAndConstantBuffer);
                        var vertexView = new VertexBufferView(
                            vertexBuffer.Resource->GetGPUVirtualAddress(),
                            checked((uint)vertexBuffer.Size),
                            24);
                        nativeCommands->IASetVertexBuffers(0, 1, &vertexView);
                        break;
                    case SilkGraphicsCommandKind.SetIndexBuffer:
                        indexBuffer = command.Buffer!;
                        indexBuffer.ThrowIfDisposed();
                        PrepareBufferState(
                            nativeCommands,
                            finalBufferStates,
                            pendingUavWrites,
                            indexBuffer,
                            ResourceStates.IndexBuffer);
                        var indexView = new IndexBufferView(
                            indexBuffer.Resource->GetGPUVirtualAddress(),
                            checked((uint)indexBuffer.Size),
                            Format.FormatR32Uint);
                        nativeCommands->IASetIndexBuffer(&indexView);
                        break;
                    case SilkGraphicsCommandKind.SetUniformBuffer:
                        uniformBuffer = command.Buffer!;
                        uniformBuffer.ThrowIfDisposed();
                        PrepareBufferState(
                            nativeCommands,
                            finalBufferStates,
                            pendingUavWrites,
                            uniformBuffer,
                            ResourceStates.VertexAndConstantBuffer);
                        if (pipeline is not null ||
                            pickPipeline is not null ||
                            selectionMaskPipeline is not null)
                        {
                            nativeCommands->SetGraphicsRootConstantBufferView(
                                0,
                                uniformBuffer.Resource->GetGPUVirtualAddress());
                        }
                        break;
                    case SilkGraphicsCommandKind.SetTexture:
                        materialTexture = command.Texture!;
                        materialTexture.ThrowIfDisposed();
                        Transition(
                            nativeCommands,
                            materialTexture.Resource,
                            GetCurrentState(finalStates, materialTexture),
                            ResourceStates.PixelShaderResource);
                        finalStates[materialTexture] =
                            ResourceStates.PixelShaderResource;
                        RecordMaterialBinding(
                            materialBindings,
                            new D3D12MaterialBinding(
                                command.Binding,
                                SilkBindingKind.SampledTexture,
                                materialTexture,
                                null));
                        break;
                    case SilkGraphicsCommandKind.SetSampler:
                        command.Sampler!.ThrowIfDisposed();
                        RecordMaterialBinding(
                            materialBindings,
                            new D3D12MaterialBinding(
                                command.Binding,
                                SilkBindingKind.Sampler,
                                null,
                                command.Sampler));
                        break;
                    case SilkGraphicsCommandKind.DrawIndexed:
                        if (!rendering || colorAttachment is null ||
                            depthAttachment is null ||
                            (pipeline is null &&
                                pickPipeline is null &&
                                selectionMaskPipeline is null) ||
                            vertexBuffer is null || indexBuffer is null ||
                            uniformBuffer is null || currentViewport is null ||
                            currentScissor is null)
                        {
                            throw new InvalidOperationException(
                                "The ordered D3D12 command stream has incomplete draw state.");
                        }
                        nativeCommands->SetGraphicsRootConstantBufferView(
                            0,
                            uniformBuffer.Resource->GetGPUVirtualAddress());
                        if (materialBindings.Count != 0 && pipeline is not null)
                        {
                            BindMaterialDescriptorTables(
                                nativeCommands,
                                descriptorHeaps,
                                pipeline.BindingLayout,
                                materialBindings,
                                materialHeaps);
                        }
                        if (pickPipeline is not null)
                        {
                            if (pickBaseToken == 0)
                            {
                                throw new InvalidOperationException(
                                    "The ordered D3D12 pick draw has no base token.");
                            }
                            SetPickRootConstants(nativeCommands, pickBaseToken);
                        }
                        nativeCommands->IASetPrimitiveTopology(
                            D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
                        nativeCommands->DrawIndexedInstanced(
                            command.IndexCount,
                            1,
                            0,
                            0,
                            0);
                        break;
                    case SilkGraphicsCommandKind.EndRendering:
                        if (!rendering || colorAttachment is null)
                        {
                            throw new InvalidOperationException(
                                "The ordered D3D12 command stream ended no rendering scope.");
                        }
                        if (selectionRenderingKind ==
                            D3D12SelectionRenderingKind.Mask)
                        {
                            if (depthAttachment is null)
                            {
                                throw new InvalidOperationException(
                                    "The ordered D3D12 selection-mask pass has no depth attachment.");
                            }
                            Transition(
                                nativeCommands,
                                colorAttachment.Resource,
                                ResourceStates.RenderTarget,
                                ResourceStates.PixelShaderResource);
                            Transition(
                                nativeCommands,
                                depthAttachment.Resource,
                                ResourceStates.DepthRead,
                                ResourceStates.PixelShaderResource);
                            finalStates[colorAttachment] =
                                ResourceStates.PixelShaderResource;
                            finalStates[depthAttachment] =
                                ResourceStates.PixelShaderResource;
                        }
                        else if (selectionRenderingKind ==
                            D3D12SelectionRenderingKind.Outline)
                        {
                            Transition(
                                nativeCommands,
                                colorAttachment.Resource,
                                ResourceStates.RenderTarget,
                                ResourceStates.CopySource);
                            finalStates[colorAttachment] =
                                ResourceStates.CopySource;
                        }
                        else if (pickPipeline is null)
                        {
                            if (depthAttachment is null)
                            {
                                throw new InvalidOperationException(
                                    "The ordered D3D12 graphics pass has no depth attachment.");
                            }
                            Transition(
                                nativeCommands,
                                colorAttachment.Resource,
                                ResourceStates.RenderTarget,
                                ResourceStates.CopySource);
                            Transition(
                                nativeCommands,
                                depthAttachment.Resource,
                                ResourceStates.DepthWrite,
                                ResourceStates.CopySource);
                            finalStates[colorAttachment] = ResourceStates.CopySource;
                            finalStates[depthAttachment] = ResourceStates.CopySource;
                        }
                        else
                        {
                            if (depthAttachment is null)
                            {
                                throw new InvalidOperationException(
                                    "The ordered D3D12 pick pass has no depth attachment.");
                            }
                            finalStates[colorAttachment] = ResourceStates.RenderTarget;
                            finalStates[depthAttachment] = ResourceStates.DepthWrite;
                        }
                        colorAttachment = null;
                        depthAttachment = null;
                        selectionRenderingKind = D3D12SelectionRenderingKind.None;
                        rendering = false;
                        break;
                    case SilkGraphicsCommandKind.SetComputePipeline:
                        computePipeline = command.ComputePipeline!;
                        computePipeline.ThrowIfDisposed();
                        nativeCommands->SetComputeRootSignature(
                            computePipeline.RootSignature);
                        nativeCommands->SetPipelineState(computePipeline.Pipeline);
                        break;
                    case SilkGraphicsCommandKind.SetStorageBuffer:
                        storageBuffer = command.Buffer!;
                        storageBuffer.ThrowIfDisposed();
                        break;
                    case SilkGraphicsCommandKind.SetComputeUniformBuffer:
                        computeUniformBuffer = command.Buffer!;
                        computeUniformBuffer.ThrowIfDisposed();
                        break;
                    case SilkGraphicsCommandKind.Dispatch:
                        if (computePipeline is null ||
                            storageBuffer is null ||
                            computeUniformBuffer is null)
                        {
                            throw new InvalidOperationException(
                                "The ordered D3D12 command stream has incomplete compute state.");
                        }
                        PrepareBufferState(
                            nativeCommands,
                            finalBufferStates,
                            pendingUavWrites,
                            storageBuffer,
                            ResourceStates.UnorderedAccess);
                        nativeCommands->SetComputeRootSignature(
                            computePipeline.RootSignature);
                        nativeCommands->SetPipelineState(computePipeline.Pipeline);
                        nativeCommands->SetComputeRootUnorderedAccessView(
                            0,
                            storageBuffer.Resource->GetGPUVirtualAddress());
                        nativeCommands->SetComputeRootConstantBufferView(
                            1,
                            computeUniformBuffer.Resource->GetGPUVirtualAddress());
                        nativeCommands->Dispatch(
                            checked((command.ElementCount + 63) / 64),
                            1,
                            1);
                        pendingUavWrites.Add(storageBuffer);
                        break;
                    case SilkGraphicsCommandKind.BufferBarrier:
                        D3D12SilkGraphicsBuffer barrierBuffer = command.Buffer!;
                        barrierBuffer.ThrowIfDisposed();
                        InsertUavBarrier(nativeCommands, barrierBuffer);
                        pendingUavWrites.Remove(barrierBuffer);
                        break;
                    default:
                        throw new InvalidOperationException("Unknown D3D12 graphics command.");
                }
            }

            SilkMarshal.ThrowHResult(nativeCommands->Close());
            if (pickExecution is null)
            {
                Guid fenceId = ID3D12Fence.Guid;
                SilkMarshal.ThrowHResult(_device->CreateFence(
                    0,
                    FenceFlags.None,
                    &fenceId,
                    (void**)&completionFence));
            }

            ID3D12CommandList* commandListPointer = (ID3D12CommandList*)nativeCommands;
            _queue->ExecuteCommandLists(1, &commandListPointer);
            workSubmitted = true;
            SilkMarshal.ThrowHResult(_queue->Signal(
                completionFence,
                completionValue));
            foreach (KeyValuePair<D3D12SilkGraphicsTexture, ResourceStates> state in finalStates)
            {
                state.Key.State = state.Value;
            }
            foreach (KeyValuePair<D3D12SilkGraphicsBuffer, ResourceStates> state in
                finalBufferStates)
            {
                state.Key.State = state.Value;
            }
            success = true;
            if (pickExecution is not null)
            {
                RecordPickSubmission();
                return new D3D12SilkPickSubmission(
                    this,
                    pickExecution,
                    completionValue,
                    [.. leases],
                    [.. uploadResources]);
            }
            return new D3D12SilkGraphicsSubmission(
                this,
                allocator,
                nativeCommands,
                completionFence,
                [.. leases],
                [.. uploadResources],
                [.. materialHeaps]);
        }
        finally
        {
            if (!success)
            {
                if (workSubmitted)
                {
                    _ = TryDrainSubmittedWork();
                }
                if (borrowedPickExecution)
                {
                    pickExecution!.CancelSubmission(completionValue);
                    completionFence = null;
                    nativeCommands = null;
                    allocator = null;
                }
                Release(ref completionFence);
                Release(ref nativeCommands);
                Release(ref allocator);
                foreach (nint resource in uploadResources)
                {
                    ID3D12Resource* pointer = (ID3D12Resource*)resource;
                    Release(ref pointer);
                }
                foreach (nint heap in materialHeaps)
                {
                    ID3D12DescriptorHeap* pointer = (ID3D12DescriptorHeap*)heap;
                    Release(ref pointer);
                }
                foreach (IDisposable lease in leases)
                {
                    lease.Dispose();
                }
                if (dependentRegistered)
                {
                    ReleaseDependentObject();
                }
            }
        }
    }

    private void CreateTextureUpload(
        D3D12SilkGraphicsTexture texture,
        ReadOnlySpan<byte> source,
        out ID3D12Resource* upload,
        out PlacedSubresourceFootprint footprint)
    {
        ResourceDesc textureDescription = texture.Resource->GetDesc();
        uint rowCount;
        ulong rowSize;
        ulong totalSize;
        PlacedSubresourceFootprint nativeFootprint = default;
        _device->GetCopyableFootprints(
            &textureDescription,
            0,
            1,
            0,
            &nativeFootprint,
            &rowCount,
            &rowSize,
            &totalSize);
        var heapProperties = new HeapProperties(HeapType.Upload);
        var uploadDescription = new ResourceDesc(
            ResourceDimension.Buffer,
            0,
            totalSize,
            1,
            1,
            1,
            Format.FormatUnknown,
            new SampleDesc(1, 0),
            TextureLayout.LayoutRowMajor,
            ResourceFlags.None);
        ID3D12Resource* nativeUpload = null;
        Guid resourceId = ID3D12Resource.Guid;
        SilkMarshal.ThrowHResult(_device->CreateCommittedResource(
            &heapProperties,
            HeapFlags.None,
            &uploadDescription,
            ResourceStates.GenericRead,
            null,
            &resourceId,
            (void**)&nativeUpload));
        try
        {
            void* mapped = null;
            var readRange = new global::Silk.NET.Direct3D12.Range(0, 0);
            SilkMarshal.ThrowHResult(nativeUpload->Map(0, &readRange, &mapped));
            try
            {
                int sourceRowPitch = checked((int)texture.Width * 4);
                int destinationRowPitch =
                    checked((int)nativeFootprint.Footprint.RowPitch);
                byte* destination =
                    (byte*)mapped + checked((nint)nativeFootprint.Offset);
                for (int row = 0; row < texture.Height; row++)
                {
                    source.Slice(
                        checked(row * sourceRowPitch),
                        sourceRowPitch).CopyTo(new Span<byte>(
                            destination + checked(row * destinationRowPitch),
                            sourceRowPitch));
                }
            }

            finally
            {
                var writtenRange = new global::Silk.NET.Direct3D12.Range(
                    0,
                    checked((nuint)totalSize));
                nativeUpload->Unmap(0, &writtenRange);
            }
            upload = nativeUpload;
            footprint = nativeFootprint;
        }
        catch
        {
            Release(ref nativeUpload);
            throw;
        }
    }

    /// <summary>
    /// Replaces any prior binding at the same slot so the last write before a draw
    /// wins, matching how the pipeline and buffer bindings already behave.
    /// </summary>
    private static void RecordMaterialBinding(
        List<D3D12MaterialBinding> bindings,
        D3D12MaterialBinding binding)
    {
        for (int index = 0; index < bindings.Count; index++)
        {
            if (bindings[index].Binding == binding.Binding)
            {
                bindings[index] = binding;
                return;
            }
        }
        bindings.Add(binding);
    }

    /// <summary>
    /// Copies each bound resource into a shader-visible table and points the matching
    /// root descriptor table at it.
    /// </summary>
    /// <remarks>
    /// D3D12 requires descriptor tables to read from shader-visible heaps, while the
    /// per-resource views live in non-shader-visible heaps, so the descriptors must be
    /// copied. Tier-2+ devices use a shared descriptor-indexed table; lower tiers fall
    /// back to the previous per-draw heap path so unsupported adapters remain correct.
    /// </remarks>
    private void BindMaterialDescriptorTables(
        ID3D12GraphicsCommandList* commands,
        ID3D12DescriptorHeap** descriptorHeaps,
        SilkBindingLayoutDescriptor layout,
        IReadOnlyList<D3D12MaterialBinding> bindings,
        List<nint> retainedHeaps)
    {
        if (MaterialDescriptorTables is { } descriptorTables &&
            TryBindSharedMaterialDescriptorTables(
                commands,
                descriptorHeaps,
                descriptorTables,
                layout,
                bindings))
        {
            return;
        }

        uint viewCount = 0;
        uint samplerCount = 0;
        foreach (D3D12MaterialBinding binding in bindings)
        {
            if (binding.Kind == SilkBindingKind.SampledTexture)
            {
                viewCount++;
            }
            else if (binding.Kind == SilkBindingKind.Sampler)
            {
                samplerCount++;
            }
        }
        ID3D12DescriptorHeap* viewHeap =
            viewCount == 0 ? null : CreateShaderVisibleHeap(
                DescriptorHeapType.CbvSrvUav,
                viewCount,
                retainedHeaps);
        ID3D12DescriptorHeap* samplerHeap =
            samplerCount == 0 ? null : CreateShaderVisibleHeap(
                DescriptorHeapType.Sampler,
                samplerCount,
                retainedHeaps);
        uint heapCount = 0;
        if (viewHeap != null)
        {
            descriptorHeaps[heapCount++] = viewHeap;
        }
        if (samplerHeap != null)
        {
            descriptorHeaps[heapCount++] = samplerHeap;
        }
        if (heapCount == 0)
        {
            return;
        }
        commands->SetDescriptorHeaps(heapCount, descriptorHeaps);
        uint viewIncrement = _device->GetDescriptorHandleIncrementSize(
            DescriptorHeapType.CbvSrvUav);
        uint samplerIncrement = _device->GetDescriptorHandleIncrementSize(
            DescriptorHeapType.Sampler);
        uint viewIndex = 0;
        uint samplerIndex = 0;
        foreach (D3D12MaterialBinding binding in bindings)
        {
            // Root parameter zero is SceneParameters, so slot i lives at i + 1.
            uint rootParameter = (uint)layout.RequireMaterialSlot(
                0,
                binding.Binding,
                binding.Kind) + 1;
            if (binding.Kind == SilkBindingKind.SampledTexture)
            {
                CpuDescriptorHandle destination = new(
                    viewHeap->GetCPUDescriptorHandleForHeapStart().Ptr +
                    (viewIndex * viewIncrement));
                _device->CopyDescriptorsSimple(
                    1,
                    destination,
                    binding.Texture!.ShaderResourceView,
                    DescriptorHeapType.CbvSrvUav);
                commands->SetGraphicsRootDescriptorTable(
                    rootParameter,
                    new GpuDescriptorHandle(
                        viewHeap->GetGPUDescriptorHandleForHeapStart().Ptr +
                        (viewIndex * viewIncrement)));
                viewIndex++;
                continue;
            }
            CpuDescriptorHandle samplerDestination = new(
                samplerHeap->GetCPUDescriptorHandleForHeapStart().Ptr +
                (samplerIndex * samplerIncrement));
            _device->CopyDescriptorsSimple(
                1,
                samplerDestination,
                binding.Sampler!.Heap->GetCPUDescriptorHandleForHeapStart(),
                DescriptorHeapType.Sampler);
            commands->SetGraphicsRootDescriptorTable(
                rootParameter,
                new GpuDescriptorHandle(
                    samplerHeap->GetGPUDescriptorHandleForHeapStart().Ptr +
                    (samplerIndex * samplerIncrement)));
            samplerIndex++;
        }
    }

    private bool TryBindSharedMaterialDescriptorTables(
        ID3D12GraphicsCommandList* commands,
        ID3D12DescriptorHeap** descriptorHeaps,
        D3D12DescriptorIndexedTextureTables descriptorTables,
        SilkBindingLayoutDescriptor layout,
        IReadOnlyList<D3D12MaterialBinding> bindings)
    {
        if (bindings.Count == 0)
        {
            return true;
        }

        var handles = new GpuDescriptorHandle[bindings.Count];
        for (int index = 0; index < bindings.Count; index++)
        {
            D3D12MaterialBinding binding = bindings[index];
            bool copied = binding.Kind == SilkBindingKind.SampledTexture
                ? descriptorTables.TryCopySampledTexture(
                    binding.Texture!.ShaderResourceView,
                    out handles[index])
                : descriptorTables.TryCopySampler(
                    binding.Sampler!.Heap->GetCPUDescriptorHandleForHeapStart(),
                    out handles[index]);
            if (!copied)
            {
                return false;
            }
        }

        uint heapCount = descriptorTables.FillDescriptorHeaps(descriptorHeaps);
        commands->SetDescriptorHeaps(heapCount, descriptorHeaps);
        for (int index = 0; index < bindings.Count; index++)
        {
            D3D12MaterialBinding binding = bindings[index];
            uint rootParameter = (uint)layout.RequireMaterialSlot(
                0,
                binding.Binding,
                binding.Kind) + 1;
            commands->SetGraphicsRootDescriptorTable(
                rootParameter,
                handles[index]);
        }
        return true;
    }

    private ID3D12DescriptorHeap* CreateShaderVisibleHeap(
        DescriptorHeapType type,
        uint count,
        List<nint> retainedHeaps)
    {
        var description = new DescriptorHeapDesc(
            type,
            count,
            DescriptorHeapFlags.ShaderVisible,
            0);
        Guid heapId = ID3D12DescriptorHeap.Guid;
        ID3D12DescriptorHeap* heap = null;
        SilkMarshal.ThrowHResult(_device->CreateDescriptorHeap(
            &description,
            &heapId,
            (void**)&heap));
        retainedHeaps.Add((nint)heap);
        return heap;
    }

    private static ResourceStates GetCurrentState(
        Dictionary<D3D12SilkGraphicsTexture, ResourceStates> states,
        D3D12SilkGraphicsTexture texture) =>
        states.TryGetValue(texture, out ResourceStates state)
            ? state
            : texture.State;

    private static ResourceStates GetCurrentState(
        Dictionary<D3D12SilkGraphicsBuffer, ResourceStates> states,
        D3D12SilkGraphicsBuffer buffer) =>
        states.TryGetValue(buffer, out ResourceStates state)
            ? state
            : buffer.State;

    private static void SetPickRootConstants(
        ID3D12GraphicsCommandList* commands,
        uint baseToken)
    {
        uint* values = stackalloc uint[4];
        values[0] = baseToken;
        values[1] = 0;
        values[2] = 0;
        values[3] = 0;
        commands->SetGraphicsRoot32BitConstants(1, 4, values, 0);
    }

    private static void PrepareBufferState(
        ID3D12GraphicsCommandList* commands,
        Dictionary<D3D12SilkGraphicsBuffer, ResourceStates> states,
        HashSet<D3D12SilkGraphicsBuffer> pendingUavWrites,
        D3D12SilkGraphicsBuffer buffer,
        ResourceStates desiredState)
    {
        ResourceStates currentState = GetCurrentState(states, buffer);
        if (buffer.Usage.HasFlag(SilkBufferUsage.Upload))
        {
            states[buffer] = currentState;
            return;
        }
        if (pendingUavWrites.Remove(buffer))
        {
            InsertUavBarrier(commands, buffer);
        }
        Transition(commands, buffer.Resource, currentState, desiredState);
        states[buffer] = desiredState;
    }

    private static void InsertUavBarrier(
        ID3D12GraphicsCommandList* commands,
        D3D12SilkGraphicsBuffer buffer)
    {
        var barrier = new ResourceBarrier(ResourceBarrierType.Uav);
        barrier.UAV = new ResourceUavBarrier(buffer.Resource);
        commands->ResourceBarrier(1, &barrier);
    }

    private static TextureAddressMode GetAddressMode(SilkSamplerAddressMode mode) =>
        mode switch
        {
            SilkSamplerAddressMode.ClampToEdge => TextureAddressMode.Clamp,
            SilkSamplerAddressMode.Repeat => TextureAddressMode.Wrap,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

    private static Filter GetFilter(
        SilkSamplerFilter minFilter,
        SilkSamplerFilter magFilter) =>
        (minFilter, magFilter) switch
        {
            (SilkSamplerFilter.Nearest, SilkSamplerFilter.Nearest) =>
                Filter.MinMagMipPoint,
            (SilkSamplerFilter.Nearest, SilkSamplerFilter.Linear) =>
                Filter.MinPointMagLinearMipPoint,
            (SilkSamplerFilter.Linear, SilkSamplerFilter.Nearest) =>
                Filter.MinLinearMagMipPoint,
            (SilkSamplerFilter.Linear, SilkSamplerFilter.Linear) =>
                Filter.MinMagLinearMipPoint,
            _ => throw new ArgumentOutOfRangeException(nameof(minFilter))
        };

    internal void Readback(
        D3D12SilkGraphicsTexture texture,
        Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_device == null || _queue == null, this);
        texture.ThrowIfDisposed();

        var sourceDescription = texture.Resource->GetDesc();
        PlacedSubresourceFootprint footprint = default;
        uint rowCount = 0;
        ulong rowSize = 0;
        ulong totalSize = 0;
        _device->GetCopyableFootprints(
            &sourceDescription,
            0,
            1,
            0,
            &footprint,
            &rowCount,
            &rowSize,
            &totalSize);

        var heapProperties = new HeapProperties(HeapType.Readback);
        var bufferDescription = new ResourceDesc(
            ResourceDimension.Buffer,
            0,
            totalSize,
            1,
            1,
            1,
            Format.FormatUnknown,
            new SampleDesc(1, 0),
            TextureLayout.LayoutRowMajor,
            ResourceFlags.None);
        ID3D12Resource* readback = null;
        ID3D12CommandAllocator* allocator = null;
        ID3D12GraphicsCommandList* commands = null;
        ID3D12Fence* fence = null;
        bool submitted = false;
        bool completed = false;
        try
        {
            Guid resourceId = ID3D12Resource.Guid;
            SilkMarshal.ThrowHResult(_device->CreateCommittedResource(
                &heapProperties,
                HeapFlags.None,
                &bufferDescription,
                ResourceStates.CopyDest,
                null,
                &resourceId,
                (void**)&readback));
            Guid allocatorId = ID3D12CommandAllocator.Guid;
            SilkMarshal.ThrowHResult(_device->CreateCommandAllocator(
                CommandListType.Direct,
                &allocatorId,
                (void**)&allocator));
            Guid commandListId = ID3D12GraphicsCommandList.Guid;
            SilkMarshal.ThrowHResult(_device->CreateCommandList(
                0,
                CommandListType.Direct,
                allocator,
                null,
                &commandListId,
                (void**)&commands));
            Guid fenceId = ID3D12Fence.Guid;
            SilkMarshal.ThrowHResult(_device->CreateFence(
                0,
                FenceFlags.None,
                &fenceId,
                (void**)&fence));

            bool transitionToCopySource = texture.State != ResourceStates.CopySource;
            if (transitionToCopySource)
            {
                Transition(commands, texture.Resource, texture.State, ResourceStates.CopySource);
            }
            var destinationLocation = new TextureCopyLocation(
                readback,
                TextureCopyType.PlacedFootprint);
            destinationLocation.PlacedFootprint = footprint;
            var sourceLocation = new TextureCopyLocation(
                texture.Resource,
                TextureCopyType.SubresourceIndex);
            sourceLocation.SubresourceIndex = 0;
            commands->CopyTextureRegion(
                &destinationLocation,
                0,
                0,
                0,
                &sourceLocation,
                null);
            SilkMarshal.ThrowHResult(commands->Close());
            ID3D12CommandList* commandListPointer = (ID3D12CommandList*)commands;
            _queue->ExecuteCommandLists(1, &commandListPointer);
            submitted = true;

            SilkMarshal.ThrowHResult(_queue->Signal(fence, 1));
            WaitForFence(fence, 1);
            completed = true;
            if (transitionToCopySource)
            {
                texture.State = ResourceStates.CopySource;
            }

            void* mapped = null;
            var readRange = new global::Silk.NET.Direct3D12.Range(
                0,
                checked((nuint)totalSize));
            SilkMarshal.ThrowHResult(readback->Map(0, &readRange, &mapped));
            try
            {
                int destinationRowPitch = checked((int)texture.Width * 4);
                int sourceRowPitch = checked((int)footprint.Footprint.RowPitch);
                for (int row = 0; row < texture.Height; row++)
                {
                    new ReadOnlySpan<byte>(
                        (byte*)mapped + checked(row * sourceRowPitch),
                        destinationRowPitch).CopyTo(
                            destination.Slice(
                                checked(row * destinationRowPitch),
                                destinationRowPitch));
                }
            }
            finally
            {
                var writtenRange = new global::Silk.NET.Direct3D12.Range(0, 0);
                readback->Unmap(0, &writtenRange);
            }
        }
        finally
        {
            if (submitted &&
                !completed &&
                !IsDeviceRemoved() &&
                !TryDrainSubmittedWork())
            {
                RetainSubmittedReadback(readback, allocator, commands, fence);
                readback = null;
                allocator = null;
                commands = null;
                fence = null;
            }
            Release(ref fence);
            Release(ref commands);
            Release(ref allocator);
            Release(ref readback);
        }
    }

    private static void Transition(
        ID3D12GraphicsCommandList* commands,
        ID3D12Resource* resource,
        ResourceStates before,
        ResourceStates after)
    {
        if (before == after)
        {
            return;
        }
        var barrier = new ResourceBarrier(ResourceBarrierType.Transition);
        barrier.Transition = new ResourceTransitionBarrier(
            resource,
            uint.MaxValue,
            before,
            after);
        commands->ResourceBarrier(1, &barrier);
    }

}

[SupportedOSPlatform("windows")]
internal sealed unsafe class D3D12SilkGraphicsTexture : SilkGraphicsTextureBase
{
    private readonly D3D12SilkGraphicsDevice _device;
    private ID3D12Resource* _resource;
    private ID3D12DescriptorHeap* _attachmentDescriptorHeap;
    private ID3D12DescriptorHeap* _shaderResourceDescriptorHeap;

    internal D3D12SilkGraphicsTexture(
        D3D12SilkGraphicsDevice device,
        ID3D12Resource* resource,
        ID3D12DescriptorHeap* attachmentDescriptorHeap,
        ID3D12DescriptorHeap* shaderResourceDescriptorHeap,
        CpuDescriptorHandle attachmentView,
        CpuDescriptorHandle readOnlyDepthView,
        CpuDescriptorHandle shaderResourceView,
        SilkTextureDescriptor descriptor)
        : base(descriptor)
    {
        _device = device;
        _resource = resource;
        _attachmentDescriptorHeap = attachmentDescriptorHeap;
        _shaderResourceDescriptorHeap = shaderResourceDescriptorHeap;
        AttachmentView = attachmentView;
        ReadOnlyDepthView = readOnlyDepthView;
        ShaderResourceView = shaderResourceView;
        State = ResourceStates.Common;
    }

    internal ID3D12Resource* Resource => _resource;

    internal D3D12SilkGraphicsDevice Device => _device;

    internal CpuDescriptorHandle AttachmentView { get; }

    internal CpuDescriptorHandle ReadOnlyDepthView { get; }

    internal CpuDescriptorHandle ShaderResourceView { get; }

    internal ResourceStates State { get; set; }

    public override void ReadbackForTesting(Span<byte> destination)
    {
        ThrowIfDisposed();
        ValidateReadback(destination.Length);
        _device.Readback(this, destination);
    }

    public override void ReadbackForTesting(Span<float> destination)
    {
        ThrowIfDisposed();
        ValidateDepthReadback(destination.Length);
        _device.Readback(this, MemoryMarshal.AsBytes(destination));
    }

    protected override void ReleaseNative()
    {
        D3D12SilkGraphicsDevice.Release(ref _shaderResourceDescriptorHeap);
        D3D12SilkGraphicsDevice.Release(ref _attachmentDescriptorHeap);
        D3D12SilkGraphicsDevice.Release(ref _resource);
        _device.ReleaseDependentObject();
    }

    internal IDisposable AcquireLease() => AcquireSubmissionLease();

    internal void ThrowIfDisposed() => ThrowIfTextureDisposed();
}

[SupportedOSPlatform("windows")]
internal sealed unsafe class D3D12SilkGraphicsSampler(
    D3D12SilkGraphicsDevice device,
    ID3D12DescriptorHeap* heap,
    SilkSamplerDescriptor descriptor)
    : ISilkGraphicsSampler
{
    private readonly D3D12SilkGraphicsDevice _device = device;
    private ID3D12DescriptorHeap* _heap = heap;

    internal D3D12SilkGraphicsDevice Device => _device;

    internal ID3D12DescriptorHeap* Heap => _heap;

    public SilkSamplerDescriptor Descriptor { get; } = descriptor;

    public void Dispose()
    {
        if (_heap == null)
        {
            return;
        }
        D3D12SilkGraphicsDevice.Release(ref _heap);
        _device.ReleaseDependentObject();
    }

    internal void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_heap == null, this);
}

[SupportedOSPlatform("windows")]
internal sealed partial class D3D12SilkGraphicsCommandList(D3D12SilkGraphicsDevice device)
    : ISilkGraphicsCommandList
{
    private readonly List<D3D12GraphicsCommand> _commands = [];
    private D3D12SilkGraphicsTexture? _colorAttachment;
    private D3D12SilkGraphicsTexture? _depthAttachment;
    private D3D12SilkGraphicsPipeline? _pipeline;
    private D3D12SilkGraphicsBuffer? _vertexBuffer;
    private D3D12SilkGraphicsBuffer? _indexBuffer;
    private D3D12SilkGraphicsBuffer? _uniformBuffer;
    private D3D12SilkComputePipeline? _computePipeline;
    private D3D12SilkGraphicsBuffer? _storageBuffer;
    private D3D12SilkGraphicsBuffer? _computeUniformBuffer;
    private SilkViewport? _viewport;
    private SilkScissor? _scissor;
    private bool _rendering;
    private bool _submitted;
    private bool _disposed;

    internal D3D12SilkGraphicsDevice Device { get; } = device;

    internal IReadOnlyList<D3D12GraphicsCommand> Commands => _commands;

    internal bool ContainsPickCopy => _pickReadbackDestination is not null;

    public void UploadTexture(ISilkGraphicsTexture texture, ReadOnlySpan<byte> source)
    {
        ThrowIfOutsideRendering();
        D3D12SilkGraphicsTexture d3d12Texture = ValidateTexture(texture);
        if (d3d12Texture.Format != SilkTextureFormat.Rgba8Unorm ||
            !d3d12Texture.Usage.HasFlag(SilkTextureUsage.CopyDestination))
        {
            throw new InvalidOperationException(
                "UploadTexture requires an RGBA8 texture with CopyDestination usage.");
        }
        int requiredLength = checked((int)(d3d12Texture.Width * d3d12Texture.Height * 4));
        if (source.Length != requiredLength)
        {
            throw new ArgumentException(
                $"The source must contain exactly {requiredLength} bytes.",
                nameof(source));
        }
        _commands.Add(D3D12GraphicsCommand.Upload(d3d12Texture, source.ToArray()));
    }

    public void ClearColor(ISilkGraphicsTexture texture, SilkColor color)
    {
        ThrowIfOutsideRendering();
        D3D12SilkGraphicsTexture d3d12Texture = ValidateTexture(texture);
        if (d3d12Texture.Format != SilkTextureFormat.Rgba8Unorm ||
            !d3d12Texture.Usage.HasFlag(SilkTextureUsage.ColorRenderTarget))
        {
            throw new InvalidOperationException("ClearColor requires an RGBA8 color render target.");
        }
        color.Validate();
        _commands.Add(D3D12GraphicsCommand.ClearColor(d3d12Texture, color));
    }

    public void ClearDepth(ISilkGraphicsTexture texture, float depth)
    {
        ThrowIfOutsideRendering();
        D3D12SilkGraphicsTexture d3d12Texture = ValidateTexture(texture);
        if (d3d12Texture.Format != SilkTextureFormat.D32Float ||
            !d3d12Texture.Usage.HasFlag(SilkTextureUsage.DepthRenderTarget))
        {
            throw new InvalidOperationException(
                "ClearDepth requires a D32Float depth render target.");
        }
        ValidateDepth(depth);
        _commands.Add(D3D12GraphicsCommand.ClearDepth(d3d12Texture, depth));
    }

    public void BeginRendering(SilkRenderingDescriptor descriptor)
    {
        ThrowIfUnavailable();
        if (_rendering)
        {
            throw new InvalidOperationException("A rendering scope is already active.");
        }
        D3D12SilkGraphicsTexture color = ValidateTexture(descriptor.ColorAttachment);
        D3D12SilkGraphicsTexture depth = ValidateTexture(descriptor.DepthAttachment);
        if (color.Format != SilkTextureFormat.Rgba8Unorm ||
            !color.Usage.HasFlag(SilkTextureUsage.ColorRenderTarget) ||
            depth.Format != SilkTextureFormat.D32Float ||
            !depth.Usage.HasFlag(SilkTextureUsage.DepthRenderTarget) ||
            color.Width != depth.Width ||
            color.Height != depth.Height)
        {
            throw new ArgumentException(
                "Rendering requires matching RGBA8 color and D32Float depth attachments.",
                nameof(descriptor));
        }
        _colorAttachment = color;
        _depthAttachment = depth;
        _rendering = true;
        _commands.Add(D3D12GraphicsCommand.BeginRendering(color, depth));
    }

    public void SetGraphicsPipeline(ISilkGraphicsPipeline pipeline)
    {
        ThrowIfRendering();
        ArgumentNullException.ThrowIfNull(pipeline);
        pipeline = pipeline is ISilkGraphicsPipelineLease lease
            ? lease.Pipeline
            : pipeline;
        if (pipeline is not D3D12SilkGraphicsPipeline d3d12Pipeline ||
            !ReferenceEquals(d3d12Pipeline.Device, Device))
        {
            throw new ArgumentException(
                "The pipeline was not created by this D3D12 device.",
                nameof(pipeline));
        }
        d3d12Pipeline.ThrowIfDisposed();
        _pickPipeline = null;
        _pickBaseToken = 0;
        _selectionMaskPipeline = null;
        _selectionOutlinePipeline = null;
        _selectionOutlineBinding = null;
        _selectionRenderingKind = D3D12SelectionRenderingKind.None;
        _pipeline = d3d12Pipeline;
        _commands.Add(D3D12GraphicsCommand.SetPipeline(d3d12Pipeline));
    }

    public void SetViewport(SilkViewport viewport)
    {
        ThrowIfRendering();
        viewport.Validate();
        _viewport = viewport;
        _commands.Add(D3D12GraphicsCommand.SetViewport(viewport));
    }

    public void SetScissor(SilkScissor scissor)
    {
        ThrowIfRendering();
        scissor.Validate();
        _scissor = scissor;
        _commands.Add(D3D12GraphicsCommand.SetScissor(scissor));
    }

    public void SetVertexBuffer(ISilkGraphicsBuffer buffer)
    {
        ThrowIfRendering();
        D3D12SilkGraphicsBuffer d3d12Buffer = ValidateBuffer(buffer);
        if (!d3d12Buffer.Usage.HasFlag(SilkBufferUsage.Vertex))
        {
            throw new ArgumentException("The buffer is not a vertex buffer.", nameof(buffer));
        }
        _vertexBuffer = d3d12Buffer;
        _commands.Add(D3D12GraphicsCommand.SetVertexBuffer(d3d12Buffer));
    }

    public void SetIndexBuffer(ISilkGraphicsBuffer buffer)
    {
        ThrowIfRendering();
        D3D12SilkGraphicsBuffer d3d12Buffer = ValidateBuffer(buffer);
        if (!d3d12Buffer.Usage.HasFlag(SilkBufferUsage.Index))
        {
            throw new ArgumentException("The buffer is not an index buffer.", nameof(buffer));
        }
        _indexBuffer = d3d12Buffer;
        _commands.Add(D3D12GraphicsCommand.SetIndexBuffer(d3d12Buffer));
    }

    public void SetUniformBuffer(
        uint setIndex,
        uint binding,
        ISilkGraphicsBuffer buffer)
    {
        ThrowIfRendering();
        D3D12SilkGraphicsBuffer d3d12Buffer = ValidateBuffer(buffer);
        if (setIndex != 0 || binding != 0 ||
            !d3d12Buffer.Usage.HasFlag(SilkBufferUsage.Uniform) ||
            d3d12Buffer.Size < 80)
        {
            throw new ArgumentException(
                "SceneParameters requires an 80-byte uniform buffer at set 0, binding 0.",
                nameof(buffer));
        }
        _uniformBuffer = d3d12Buffer;
        _commands.Add(D3D12GraphicsCommand.SetUniformBuffer(
            setIndex,
            binding,
            d3d12Buffer));
    }

    public void SetTexture(uint setIndex, uint binding, ISilkGraphicsTexture texture)
    {
        ThrowIfRendering();
        D3D12SilkGraphicsTexture d3d12Texture = ValidateTexture(texture);
        RequireMaterialSlot(setIndex, binding, SilkBindingKind.SampledTexture);
        if (!d3d12Texture.Usage.HasFlag(SilkTextureUsage.Sampled))
        {
            throw new ArgumentException(
                "A sampled-texture slot requires a texture with Sampled usage.",
                nameof(texture));
        }
        _commands.Add(D3D12GraphicsCommand.SetTexture(
            setIndex,
            binding,
            d3d12Texture));
    }

    public void SetSampler(uint setIndex, uint binding, ISilkGraphicsSampler sampler)
    {
        ThrowIfRendering();
        ArgumentNullException.ThrowIfNull(sampler);
        if (sampler is not D3D12SilkGraphicsSampler d3d12Sampler ||
            !ReferenceEquals(d3d12Sampler.Device, Device))
        {
            throw new ArgumentException(
                "The sampler must belong to this D3D12 device.",
                nameof(sampler));
        }
        d3d12Sampler.ThrowIfDisposed();
        RequireMaterialSlot(setIndex, binding, SilkBindingKind.Sampler);
        _commands.Add(D3D12GraphicsCommand.SetSampler(
            setIndex,
            binding,
            d3d12Sampler));
    }

    /// <summary>
    /// Requires that the bound pipeline declares a matching material slot, so a
    /// binding that no pipeline can consume is rejected while recording rather than
    /// silently dropped at submission.
    /// </summary>
    private void RequireMaterialSlot(uint setIndex, uint binding, SilkBindingKind kind)
    {
        if (_pipeline is null)
        {
            throw new InvalidOperationException(
                "A material resource can only be bound after a graphics pipeline.");
        }
        _ = _pipeline.BindingLayout.RequireMaterialSlot(setIndex, binding, kind);
    }

    public void DrawIndexed(uint indexCount)
    {
        ThrowIfRendering();
        ArgumentOutOfRangeException.ThrowIfZero(indexCount);
        if (_colorAttachment is null || _depthAttachment is null ||
            (_pipeline is null &&
                _pickPipeline is null &&
                _selectionMaskPipeline is null) ||
            _vertexBuffer is null ||
            _indexBuffer is null || _uniformBuffer is null ||
            _viewport is null || _scissor is null)
        {
            throw new InvalidOperationException(
                "Indexed drawing requires attachments, pipeline, viewport, scissor, and all buffers.");
        }
        if (_pickPipeline is not null && _pickBaseToken == 0)
        {
            throw new InvalidOperationException(
                "Indexed pick drawing requires a nonzero base token.");
        }
        if (checked((nuint)indexCount * 2) > _indexBuffer.Size)
        {
            throw new ArgumentOutOfRangeException(nameof(indexCount));
        }
        _commands.Add(D3D12GraphicsCommand.DrawIndexed(indexCount));
    }

    public void EndRendering()
    {
        ThrowIfRendering();
        _commands.Add(D3D12GraphicsCommand.EndRendering());
        _rendering = false;
        _colorAttachment = null;
        _depthAttachment = null;
        _selectionRenderingKind = D3D12SelectionRenderingKind.None;
    }

    public void SetComputePipeline(ISilkComputePipeline pipeline)
    {
        ThrowIfOutsideRendering();
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not D3D12SilkComputePipeline d3d12Pipeline ||
            !ReferenceEquals(d3d12Pipeline.Device, Device))
        {
            throw new ArgumentException(
                "The compute pipeline was not created by this D3D12 device.",
                nameof(pipeline));
        }
        d3d12Pipeline.ThrowIfDisposed();
        _computePipeline = d3d12Pipeline;
        _commands.Add(D3D12GraphicsCommand.SetComputePipeline(d3d12Pipeline));
    }

    public void SetStorageBuffer(
        uint setIndex,
        uint binding,
        ISilkGraphicsBuffer buffer)
    {
        ThrowIfOutsideRendering();
        D3D12SilkGraphicsBuffer d3d12Buffer = ValidateBuffer(buffer);
        if (setIndex != 0 || binding != 0 ||
            !d3d12Buffer.Usage.HasFlag(SilkBufferUsage.Storage))
        {
            throw new ArgumentException(
                "outputValues requires a storage buffer at set 0, binding 0.",
                nameof(buffer));
        }
        _storageBuffer = d3d12Buffer;
        _commands.Add(D3D12GraphicsCommand.SetStorageBuffer(
            setIndex,
            binding,
            d3d12Buffer));
    }

    public void SetComputeUniformBuffer(
        uint setIndex,
        uint binding,
        ISilkGraphicsBuffer buffer)
    {
        ThrowIfOutsideRendering();
        D3D12SilkGraphicsBuffer d3d12Buffer = ValidateBuffer(buffer);
        if (setIndex != 0 || binding != 1 ||
            !d3d12Buffer.Usage.HasFlag(SilkBufferUsage.Uniform) ||
            d3d12Buffer.Size < SilkCheckedShaderAssets.Compute.D3DUniformByteSize)
        {
            throw new ArgumentException(
                "ComputeParameters requires an 8-byte uniform buffer at set 0, binding 1.",
                nameof(buffer));
        }
        _computeUniformBuffer = d3d12Buffer;
        _commands.Add(D3D12GraphicsCommand.SetComputeUniformBuffer(
            setIndex,
            binding,
            d3d12Buffer));
    }

    public void Dispatch(uint elementCount)
    {
        ThrowIfOutsideRendering();
        ArgumentOutOfRangeException.ThrowIfZero(elementCount);
        if (_computePipeline is null ||
            _storageBuffer is null ||
            _computeUniformBuffer is null)
        {
            throw new InvalidOperationException(
                "Dispatch requires a compute pipeline, storage buffer, and uniform buffer.");
        }
        if (checked((nuint)elementCount * 16) > _storageBuffer.Size)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elementCount),
                "The storage buffer is too small for the dispatch.");
        }
        _commands.Add(D3D12GraphicsCommand.Dispatch(elementCount));
    }

    public void BufferBarrier(ISilkGraphicsBuffer buffer)
    {
        ThrowIfOutsideRendering();
        D3D12SilkGraphicsBuffer d3d12Buffer = ValidateBuffer(buffer);
        if (!d3d12Buffer.Usage.HasFlag(SilkBufferUsage.Storage))
        {
            throw new ArgumentException(
                "BufferBarrier requires a storage buffer.",
                nameof(buffer));
        }
        _commands.Add(D3D12GraphicsCommand.BufferBarrier(d3d12Buffer));
    }

    public void Dispose()
    {
        _commands.Clear();
        _pickPipeline = null;
        _pickReadbackDestination = null;
        _pickBaseToken = 0;
        _selectionMaskPipeline = null;
        _selectionOutlinePipeline = null;
        _selectionOutlineBinding = null;
        _selectionRenderingKind = D3D12SelectionRenderingKind.None;
        _disposed = true;
    }

    internal void MarkSubmitted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_submitted)
        {
            throw new InvalidOperationException("The command list was already submitted.");
        }
        if (_rendering)
        {
            throw new InvalidOperationException("EndRendering must be called before submission.");
        }
        _submitted = true;
    }

    private static void ValidateDepth(float depth)
    {
        if (!float.IsFinite(depth) || depth < 0 || depth > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(depth));
        }
    }

    private D3D12SilkGraphicsTexture ValidateTexture(ISilkGraphicsTexture texture)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_submitted)
        {
            throw new InvalidOperationException("The command list was already submitted.");
        }
        ArgumentNullException.ThrowIfNull(texture);
        if (texture is not D3D12SilkGraphicsTexture d3d12Texture)
        {
            throw new ArgumentException("The texture is not a D3D12 texture.", nameof(texture));
        }
        if (!ReferenceEquals(d3d12Texture.Device, Device))
        {
            throw new ArgumentException(
                "The texture was not created by this D3D12 device.",
                nameof(texture));
        }
        d3d12Texture.ThrowIfDisposed();
        return d3d12Texture;
    }

    private D3D12SilkGraphicsBuffer ValidateBuffer(ISilkGraphicsBuffer buffer)
    {
        ThrowIfUnavailable();
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer is not D3D12SilkGraphicsBuffer d3d12Buffer ||
            !ReferenceEquals(d3d12Buffer.Device, Device))
        {
            throw new ArgumentException("The buffer is not a D3D12 buffer.", nameof(buffer));
        }
        d3d12Buffer.ThrowIfDisposed();
        return d3d12Buffer;
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_submitted)
        {
            throw new InvalidOperationException("The command list was already submitted.");
        }
    }

    private void ThrowIfRendering()
    {
        ThrowIfUnavailable();
        if (!_rendering)
        {
            throw new InvalidOperationException("No rendering scope is active.");
        }
    }

    private void ThrowIfOutsideRendering()
    {
        ThrowIfUnavailable();
        if (_rendering)
        {
            throw new InvalidOperationException(
                "Upload and clear commands cannot be recorded inside a rendering scope.");
        }
    }
}

/// <summary>One resolved material slot binding recorded before a draw.</summary>
internal readonly record struct D3D12MaterialBinding(
    uint Binding,
    SilkBindingKind Kind,
    D3D12SilkGraphicsTexture? Texture,
    D3D12SilkGraphicsSampler? Sampler);

internal readonly record struct D3D12GraphicsCommand(
    SilkGraphicsCommandKind Kind,

    D3D12SilkGraphicsTexture? Texture,
    D3D12SilkGraphicsTexture? DepthTexture,
    D3D12SilkGraphicsPipeline? Pipeline,
    D3D12SilkPickGraphicsPipeline? PickPipeline,
    D3D12SilkPickReadbackBuffer? PickReadback,
    D3D12SilkSelectionMaskGraphicsPipeline? SelectionMaskPipeline,
    D3D12SilkSelectionOutlineGraphicsPipeline? SelectionOutlinePipeline,
    D3D12SilkSelectionOutlineBinding? SelectionOutlineBinding,
    D3D12SilkComputePipeline? ComputePipeline,
    D3D12SilkGraphicsBuffer? Buffer,
    D3D12SilkGraphicsSampler? Sampler,
    D3D12PickCommandKind PickKind,
    D3D12SelectionOutlineCommandKind SelectionOutlineKind,
    SilkTexturePixelCoordinate PickCoordinate,
    uint PickBaseToken,
    SilkColor Color,
    float Depth,
    byte[]? Data,
    SilkViewport Viewport,
    SilkScissor Scissor,
    uint SetIndex,
    uint Binding,
    uint IndexCount,
    uint ElementCount)
{
    internal static D3D12GraphicsCommand Upload(
        D3D12SilkGraphicsTexture texture,
        byte[] data) =>
        Create(
            SilkGraphicsCommandKind.UploadTexture,
            texture: texture,
            data: data);

    internal static D3D12GraphicsCommand ClearColor(
        D3D12SilkGraphicsTexture texture,
        SilkColor color) =>
        Create(
            SilkGraphicsCommandKind.ClearColor,
            texture: texture,
            color: color);

    internal static D3D12GraphicsCommand ClearDepth(
        D3D12SilkGraphicsTexture texture,
        float depth) =>
        Create(
            SilkGraphicsCommandKind.ClearDepth,
            texture: texture,
            depth: depth);

    internal static D3D12GraphicsCommand BeginRendering(
        D3D12SilkGraphicsTexture color,
        D3D12SilkGraphicsTexture depth) =>
        Create(
            SilkGraphicsCommandKind.BeginRendering,
            texture: color,
            depthTexture: depth);

    internal static D3D12GraphicsCommand BeginSelectionMask(
        D3D12SilkGraphicsTexture mask,
        D3D12SilkGraphicsTexture depth) =>
        Create(
            SilkGraphicsCommandKind.BeginRendering,
            texture: mask,
            depthTexture: depth,
            selectionOutlineKind: D3D12SelectionOutlineCommandKind.BeginMask);

    internal static D3D12GraphicsCommand BeginSelectionOutline(
        D3D12SilkGraphicsTexture color) =>
        Create(
            SilkGraphicsCommandKind.BeginRendering,
            texture: color,
            selectionOutlineKind: D3D12SelectionOutlineCommandKind.BeginOutline);

    internal static D3D12GraphicsCommand SetPipeline(
        D3D12SilkGraphicsPipeline pipeline) =>
        Create(
            SilkGraphicsCommandKind.SetGraphicsPipeline,
            pipeline: pipeline);

    internal static D3D12GraphicsCommand SetPickPipeline(
        D3D12SilkPickGraphicsPipeline pipeline) =>
        Create(
            SilkGraphicsCommandKind.SetGraphicsPipeline,
            pickKind: D3D12PickCommandKind.SetPipeline,
            pickPipeline: pipeline);

    internal static D3D12GraphicsCommand SetPickBaseToken(uint baseToken) =>
        Create(
            SilkGraphicsCommandKind.SetGraphicsPipeline,
            pickKind: D3D12PickCommandKind.SetBaseToken,
            pickBaseToken: baseToken);

    internal static D3D12GraphicsCommand SetSelectionMaskPipeline(
        D3D12SilkSelectionMaskGraphicsPipeline pipeline) =>
        Create(
            SilkGraphicsCommandKind.SetGraphicsPipeline,
            selectionMaskPipeline: pipeline,
            selectionOutlineKind:
                D3D12SelectionOutlineCommandKind.SetMaskPipeline);

    internal static D3D12GraphicsCommand SetSelectionOutlinePipeline(
        D3D12SilkSelectionOutlineGraphicsPipeline pipeline) =>
        Create(
            SilkGraphicsCommandKind.SetGraphicsPipeline,
            selectionOutlinePipeline: pipeline,
            selectionOutlineKind:
                D3D12SelectionOutlineCommandKind.SetOutlinePipeline);

    internal static D3D12GraphicsCommand SetSelectionOutlineBinding(
        D3D12SilkSelectionOutlineBinding binding) =>
        Create(
            SilkGraphicsCommandKind.SetUniformBuffer,
            selectionOutlineBinding: binding,
            selectionOutlineKind: D3D12SelectionOutlineCommandKind.SetBinding);

    internal static D3D12GraphicsCommand
        DrawSelectionOutlineFullscreenTriangle() =>
        Create(
            SilkGraphicsCommandKind.DrawIndexed,
            selectionOutlineKind:
                D3D12SelectionOutlineCommandKind.DrawFullscreenTriangle);

    internal static D3D12GraphicsCommand CopyPickPixel(
        D3D12SilkGraphicsTexture source,
        SilkTexturePixelCoordinate coordinate,
        D3D12SilkPickReadbackBuffer destination) =>
        Create(
            SilkGraphicsCommandKind.EndRendering,
            texture: source,
            pickKind: D3D12PickCommandKind.CopyPixel,
            pickCoordinate: coordinate,
            pickReadback: destination);

    internal static D3D12GraphicsCommand SetViewport(SilkViewport viewport) =>
        Create(SilkGraphicsCommandKind.SetViewport, viewport: viewport);

    internal static D3D12GraphicsCommand SetScissor(SilkScissor scissor) =>
        Create(SilkGraphicsCommandKind.SetScissor, scissor: scissor);

    internal static D3D12GraphicsCommand SetVertexBuffer(
        D3D12SilkGraphicsBuffer buffer) =>
        Create(SilkGraphicsCommandKind.SetVertexBuffer, buffer: buffer);

    internal static D3D12GraphicsCommand SetIndexBuffer(
        D3D12SilkGraphicsBuffer buffer) =>
        Create(SilkGraphicsCommandKind.SetIndexBuffer, buffer: buffer);

    internal static D3D12GraphicsCommand SetUniformBuffer(
        uint setIndex,
        uint binding,
        D3D12SilkGraphicsBuffer buffer) =>
        Create(
            SilkGraphicsCommandKind.SetUniformBuffer,
            buffer: buffer,
            setIndex: setIndex,
            binding: binding);

    internal static D3D12GraphicsCommand SetTexture(
        uint setIndex,
        uint binding,
        D3D12SilkGraphicsTexture texture) =>
        Create(
            SilkGraphicsCommandKind.SetTexture,
            texture: texture,
            setIndex: setIndex,
            binding: binding);

    internal static D3D12GraphicsCommand SetSampler(
        uint setIndex,
        uint binding,
        D3D12SilkGraphicsSampler sampler) =>
        Create(
            SilkGraphicsCommandKind.SetSampler,
            sampler: sampler,
            setIndex: setIndex,
            binding: binding);

    internal static D3D12GraphicsCommand DrawIndexed(uint indexCount) =>
        Create(SilkGraphicsCommandKind.DrawIndexed, indexCount: indexCount);

    internal static D3D12GraphicsCommand EndRendering() =>
        Create(SilkGraphicsCommandKind.EndRendering);

    internal static D3D12GraphicsCommand SetComputePipeline(
        D3D12SilkComputePipeline pipeline) =>
        Create(
            SilkGraphicsCommandKind.SetComputePipeline,
            computePipeline: pipeline);

    internal static D3D12GraphicsCommand SetStorageBuffer(
        uint setIndex,
        uint binding,
        D3D12SilkGraphicsBuffer buffer) =>
        Create(
            SilkGraphicsCommandKind.SetStorageBuffer,
            buffer: buffer,
            setIndex: setIndex,
            binding: binding);

    internal static D3D12GraphicsCommand SetComputeUniformBuffer(
        uint setIndex,
        uint binding,
        D3D12SilkGraphicsBuffer buffer) =>
        Create(
            SilkGraphicsCommandKind.SetComputeUniformBuffer,
            buffer: buffer,
            setIndex: setIndex,
            binding: binding);

    internal static D3D12GraphicsCommand Dispatch(uint elementCount) =>
        Create(SilkGraphicsCommandKind.Dispatch, elementCount: elementCount);

    internal static D3D12GraphicsCommand BufferBarrier(
        D3D12SilkGraphicsBuffer buffer) =>
        Create(SilkGraphicsCommandKind.BufferBarrier, buffer: buffer);

    private static D3D12GraphicsCommand Create(
        SilkGraphicsCommandKind kind,
        D3D12SilkGraphicsTexture? texture = null,
        D3D12SilkGraphicsTexture? depthTexture = null,
        D3D12SilkGraphicsPipeline? pipeline = null,
        D3D12SilkPickGraphicsPipeline? pickPipeline = null,
        D3D12SilkPickReadbackBuffer? pickReadback = null,
        D3D12SilkSelectionMaskGraphicsPipeline? selectionMaskPipeline = null,
        D3D12SilkSelectionOutlineGraphicsPipeline? selectionOutlinePipeline = null,
        D3D12SilkSelectionOutlineBinding? selectionOutlineBinding = null,
        D3D12SilkComputePipeline? computePipeline = null,
        D3D12SilkGraphicsBuffer? buffer = null,
        D3D12SilkGraphicsSampler? sampler = null,
        D3D12PickCommandKind pickKind = D3D12PickCommandKind.None,
        D3D12SelectionOutlineCommandKind selectionOutlineKind =
            D3D12SelectionOutlineCommandKind.None,
        SilkTexturePixelCoordinate pickCoordinate = default,
        uint pickBaseToken = 0,
        SilkColor color = default,
        float depth = 0,
        byte[]? data = null,
        SilkViewport viewport = default,
        SilkScissor scissor = default,
        uint setIndex = 0,
        uint binding = 0,
        uint indexCount = 0,
        uint elementCount = 0) =>
        new(
            kind,
            texture,
            depthTexture,
            pipeline,
            pickPipeline,
            pickReadback,
            selectionMaskPipeline,
            selectionOutlinePipeline,
            selectionOutlineBinding,
            computePipeline,
            buffer,
            sampler,
            pickKind,
            selectionOutlineKind,
            pickCoordinate,
            pickBaseToken,
            color,
            depth,
            data,
            viewport,
            scissor,
            setIndex,
            binding,
            indexCount,
            elementCount);
}

[SupportedOSPlatform("windows")]
internal sealed unsafe class D3D12SilkGraphicsSubmission(
    D3D12SilkGraphicsDevice device,
    ID3D12CommandAllocator* allocator,
    ID3D12GraphicsCommandList* commands,
    ID3D12Fence* fence,
    IDisposable[] leases,
    nint[] uploadResources,
    nint[] descriptorHeaps)
    : ISilkGraphicsSubmission
{
    private readonly D3D12SilkGraphicsDevice _device = device;
    private ID3D12CommandAllocator* _allocator = allocator;
    private ID3D12GraphicsCommandList* _commands = commands;
    private ID3D12Fence* _fence = fence;
    private IDisposable[]? _leases = leases;
    private nint[]? _uploadResources = uploadResources;
    private nint[]? _descriptorHeaps = descriptorHeaps;

    public bool IsCompleted
    {
        get
        {
            ObjectDisposedException.ThrowIf(_fence == null, this);
            bool completed = _device.IsFenceCompleted(_fence, 1);
            if (completed)
            {
                ReleaseLeases();
            }
            return completed;
        }
    }

    public void Wait()
    {
        ObjectDisposedException.ThrowIf(_fence == null, this);
        _device.WaitForFence(_fence, 1);
        ReleaseLeases();
    }

    public void Dispose()
    {
        if (_fence == null)
        {
            return;
        }
        Wait();
        D3D12SilkGraphicsDevice.Release(ref _fence);
        D3D12SilkGraphicsDevice.Release(ref _commands);
        D3D12SilkGraphicsDevice.Release(ref _allocator);
        _device.ReleaseDependentObject();
    }

    private void ReleaseLeases()
    {
        IDisposable[]? leases = Interlocked.Exchange(ref _leases, null);
        if (leases is null)
        {
            return;
        }
        foreach (IDisposable lease in leases)
        {
            lease.Dispose();
        }
        nint[]? resources = Interlocked.Exchange(ref _uploadResources, null);
        if (resources is null)
        {
            return;
        }
        foreach (nint resource in resources)
        {
            ID3D12Resource* pointer = (ID3D12Resource*)resource;
            D3D12SilkGraphicsDevice.Release(ref pointer);
        }
        nint[]? heaps = Interlocked.Exchange(ref _descriptorHeaps, null);
        if (heaps is null)
        {
            return;
        }
        foreach (nint heap in heaps)
        {
            ID3D12DescriptorHeap* pointer = (ID3D12DescriptorHeap*)heap;
            D3D12SilkGraphicsDevice.Release(ref pointer);
        }
    }
}
