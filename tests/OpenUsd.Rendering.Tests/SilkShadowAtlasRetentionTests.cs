// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Pins what invalidates the retained shadow atlas and what happens when the
/// device refuses to allocate its replacement.
/// </summary>
/// <remarks>
/// <para>
/// The atlas is deliberately reused byte for byte across frames, so the only
/// thing standing between a correct image and a stale one is the set of inputs
/// the cache keys on. A device reset drops every image the device held,
/// whichever submission detected it, so the key has to move for a loss reported
/// by an ordinary colour or shadow submission -- not only for one that happened
/// to invalidate the selection-outline resources.
/// </para>
/// <para>
/// The failure path matters for the same reason from the other direction. The
/// colour pass binds the atlas field on every frame of every scene, so a
/// refused allocation that left that field pointing at the image it had just
/// released would be a use-after-free on the very next frame rather than a
/// missing shadow.
/// </para>
/// </remarks>
public sealed class SilkShadowAtlasRetentionTests
{
    [Test]
    public async Task AReusedAtlasIsRenderedOnceUntilSomethingChanges()
    {
        using var fixture = new ShadowFixture();

        fixture.Prepare();
        fixture.Prepare();
        fixture.Prepare();

        await Assert.That(fixture.Cache.RenderCount).IsEqualTo(1ul);
        await Assert.That(fixture.Cache.MapCount).IsEqualTo(1);
        await Assert.That(fixture.Device.CreatedShadowTextureCount).IsEqualTo(1);
    }

    [Test]
    public async Task AVulkanLossOnAnOrdinarySubmissionRerendersTheAtlas()
    {
        // The regression. A Vulkan device that loses itself on an ordinary
        // colour or shadow submission advances only its device-loss generation:
        // the picking and selection-outline generations belong to subsystems
        // this submission never touched. A cache keyed on the outline
        // generation alone would reuse an atlas whose image the reset
        // destroyed, and the frame would sample a dead descriptor.
        using var fixture = new ShadowFixture(SilkGraphicsBackend.Vulkan);
        fixture.Prepare();
        fixture.Prepare();
        ulong reused = fixture.Cache.RenderCount;

        fixture.Device.DeviceLossGeneration++;
        fixture.Prepare();

        await Assert.That(reused).IsEqualTo(1ul);
        await Assert.That(fixture.Device.SelectionOutlineDeviceGeneration)
            .IsEqualTo(0ul)
            .Because("An ordinary submission's loss touches no outline resource.");
        await Assert.That(fixture.Cache.RenderCount).IsEqualTo(2ul);
    }

    [Test]
    public async Task RepeatedD3DLossRebuildsTheAtlasEveryTime()
    {
        // Once is not enough: a cache that mixed the generations but stored the
        // wrong one would recover from the first reset and then reuse a dead
        // atlas from the second onwards.
        using var fixture = new ShadowFixture(SilkGraphicsBackend.D3D12);
        fixture.Prepare();

        var counts = new List<ulong>();
        for (int loss = 0; loss < 3; loss++)
        {
            fixture.Device.DeviceLossGeneration++;
            fixture.Prepare();

            // The frame after a reset changes nothing and must reuse again, so
            // a cache that simply never reused would not pass this either.
            fixture.Prepare();
            counts.Add(fixture.Cache.RenderCount);
        }

        await Assert.That(counts).IsEquivalentTo(new List<ulong> { 2, 3, 4 });
    }

    [Test]
    public async Task ASelectionOutlineResetAloneStillRerendersTheAtlas()
    {
        // The case the previous key did cover, kept covered: mixing the
        // generations must not drop a reset that only the outline subsystem
        // reported.
        using var fixture = new ShadowFixture();
        fixture.Prepare();
        fixture.Prepare();

        fixture.Device.SelectionOutlineDeviceGeneration++;
        fixture.Prepare();

        await Assert.That(fixture.Cache.RenderCount).IsEqualTo(2ul);
    }

    [Test]
    public async Task ARefusedFirstAllocationLeavesNoAtlasAndRecoversNextFrame()
    {
        using var fixture = new ShadowFixture();
        fixture.Device.RefuseNextTexture = true;

        InvalidOperationException? refused = null;
        try
        {
            fixture.Prepare();
        }
        catch (InvalidOperationException exception)
        {
            refused = exception;
        }

        // Nothing was published, so the colour pass binds the one-texel
        // stand-in rather than a half-created atlas.
        ShadowTexture bound = fixture.BindAndCaptureAtlas();
        await Assert.That(refused).IsNotNull();
        await Assert.That(bound.IsDisposed).IsFalse();
        await Assert.That(bound.Width).IsEqualTo(1u);
        await Assert.That(fixture.Cache.RenderCount).IsEqualTo(0ul);

        fixture.Prepare();

        ShadowTexture recovered = fixture.BindAndCaptureAtlas();
        await Assert.That(fixture.Cache.RenderCount).IsEqualTo(1ul);
        await Assert.That(recovered.IsDisposed).IsFalse();

        // One 512-texel map is placed in a two-by-two atlas, so the image the
        // cache allocates is 1024 texels on a side.
        await Assert.That(recovered.Width).IsEqualTo(1024u);
    }

    [Test]
    public async Task ARefusedReallocationNeverLeavesTheDisposedAtlasBound()
    {
        // The resolution change is the only path that releases the retained
        // image, and it is exactly the path a refused allocation can interrupt.
        // Every texture this device ever handed out must be either alive or
        // unreferenced afterwards -- never both disposed and bound.
        using var fixture = new ShadowFixture();
        fixture.Prepare();
        ShadowTexture original = fixture.BindAndCaptureAtlas();

        fixture.Resolution = 1024;
        fixture.Device.RefuseNextTexture = true;
        InvalidOperationException? refused = null;
        try
        {
            fixture.Prepare();
        }
        catch (InvalidOperationException exception)
        {
            refused = exception;
        }

        ShadowTexture afterFailure = fixture.BindAndCaptureAtlas();
        await Assert.That(refused).IsNotNull();
        await Assert.That(original.Width).IsEqualTo(1024u);
        await Assert.That(original.IsDisposed).IsTrue();
        await Assert.That(afterFailure).IsNotEqualTo(original);
        await Assert.That(afterFailure.IsDisposed).IsFalse();
        await Assert.That(afterFailure.Width).IsEqualTo(1u);

        // The retry allocates the resolution the descriptor now asks for, binds
        // it, and renders into it.
        fixture.Prepare();

        ShadowTexture recovered = fixture.BindAndCaptureAtlas();
        await Assert.That(recovered.IsDisposed).IsFalse();
        await Assert.That(recovered.Width).IsEqualTo(2048u);
        await Assert.That(fixture.Cache.RenderCount).IsEqualTo(2ul);
        await Assert.That(fixture.Cache.AtlasEdge).IsEqualTo(2048u);
    }

    [Test]
    public async Task ARefusedReallocationAfterALossStillRecovers()
    {
        // The two failure paths compose: a reset invalidates the atlas, the
        // allocation that would replace it is refused, and the frame after that
        // must still end up with a live image rather than a disposed one.
        using var fixture = new ShadowFixture(SilkGraphicsBackend.Vulkan);
        fixture.Prepare();

        fixture.Device.DeviceLossGeneration++;
        fixture.Resolution = 256;
        fixture.Device.RefuseNextTexture = true;
        try
        {
            fixture.Prepare();
        }
        catch (InvalidOperationException)
        {
            // Expected: the one-shot refusal.
        }

        await Assert.That(fixture.BindAndCaptureAtlas().IsDisposed).IsFalse();

        fixture.Prepare();
        ShadowTexture recovered = fixture.BindAndCaptureAtlas();

        await Assert.That(recovered.IsDisposed).IsFalse();
        await Assert.That(recovered.Width).IsEqualTo(512u);
        await Assert.That(fixture.Cache.RenderCount).IsEqualTo(2ul);
    }

    [Test]
    public async Task AVulkanLossOnAnOrdinarySubmissionReallocatesTheSameSizedAtlas()
    {
        // Re-rendering is not enough. The image a lost Vulkan device held is
        // gone whatever its dimensions were, so an atlas whose edge length the
        // next frame happens to match is still a destroyed image: the cache has
        // to release it and allocate a replacement rather than record a depth
        // pass into the handle it already had.
        using var fixture = new ShadowFixture(SilkGraphicsBackend.Vulkan);
        fixture.Prepare();
        (ShadowTexture original, ShadowSampler originalSampler) = fixture.BindAndCapture();

        fixture.Device.DeviceLossGeneration++;
        fixture.Prepare();
        (ShadowTexture replacement, ShadowSampler replacementSampler) =
            fixture.BindAndCapture();

        await Assert.That(fixture.Device.SelectionOutlineDeviceGeneration)
            .IsEqualTo(0ul)
            .Because("An ordinary submission's loss touches no outline resource.");
        await Assert.That(original.IsDisposed).IsTrue();
        await Assert.That(replacement).IsNotEqualTo(original);
        await Assert.That(replacement.IsDisposed).IsFalse();
        await Assert.That(replacement.Width)
            .IsEqualTo(original.Width)
            .Because("The descriptor is unchanged, so the replacement is the same size.");
        await Assert.That(fixture.Device.CreatedShadowTextureCount).IsEqualTo(2);

        // The sampler is the object most easily forgotten, because it is created
        // once on the first bind and never looked at again.
        await Assert.That(originalSampler.IsDisposed).IsTrue();
        await Assert.That(replacementSampler).IsNotEqualTo(originalSampler);
        await Assert.That(replacementSampler.IsDisposed).IsFalse();
    }

    [Test]
    public async Task RepeatedD3DLossReallocatesAtTheSameResolutionEveryTime()
    {
        // Every reset, not just the first, and at a resolution that never
        // changes -- which is precisely the case a size-keyed reallocation
        // misses.
        using var fixture = new ShadowFixture(SilkGraphicsBackend.D3D12);
        fixture.Prepare();
        ShadowTexture current = fixture.BindAndCaptureAtlas();

        for (int loss = 0; loss < 3; loss++)
        {
            fixture.Device.DeviceLossGeneration++;
            fixture.Prepare();
            ShadowTexture replacement = fixture.BindAndCaptureAtlas();

            await Assert.That(current.IsDisposed).IsTrue();
            await Assert.That(replacement).IsNotEqualTo(current);
            await Assert.That(replacement.IsDisposed).IsFalse();
            await Assert.That(replacement.Width).IsEqualTo(1024u);
            current = replacement;

            // A frame that changes nothing must still reuse, so a cache that
            // reallocated unconditionally would not pass this either.
            fixture.Prepare();
            await Assert.That(fixture.BindAndCaptureAtlas()).IsEqualTo(current);
        }

        await Assert.That(fixture.Device.CreatedShadowTextureCount).IsEqualTo(4);
    }

    [Test]
    public async Task ARefusedAllocationAfterALossAtTheSameSizeRecoversNextFrame()
    {
        // The composed failure at an unchanged resolution: the reset releases
        // the atlas, the allocation that would replace it is refused, and what
        // the colour pass binds in between must be a live one-texel stand-in
        // rather than the image the reset invalidated.
        using var fixture = new ShadowFixture(SilkGraphicsBackend.D3D12);
        fixture.Prepare();
        ShadowTexture original = fixture.BindAndCaptureAtlas();

        fixture.Device.DeviceLossGeneration++;
        fixture.Device.RefuseNextTexture = true;
        InvalidOperationException? refused = null;
        try
        {
            fixture.Prepare();
        }
        catch (InvalidOperationException exception)
        {
            refused = exception;
        }

        (ShadowTexture afterFailure, ShadowSampler afterFailureSampler) =
            fixture.BindAndCapture();
        await Assert.That(refused).IsNotNull();
        await Assert.That(original.IsDisposed).IsTrue();
        await Assert.That(afterFailure).IsNotEqualTo(original);
        await Assert.That(afterFailure.IsDisposed).IsFalse();
        await Assert.That(afterFailure.Width).IsEqualTo(1u);
        await Assert.That(afterFailureSampler.IsDisposed).IsFalse();
        await Assert.That(fixture.Cache.RenderCount).IsEqualTo(1ul);

        fixture.Prepare();
        ShadowTexture recovered = fixture.BindAndCaptureAtlas();

        await Assert.That(recovered.IsDisposed).IsFalse();
        await Assert.That(recovered.Width).IsEqualTo(1024u);
        await Assert.That(fixture.Cache.RenderCount).IsEqualTo(2ul);
        await Assert.That(fixture.Cache.AtlasEdge).IsEqualTo(1024u);
    }

    [Test]
    public async Task ALossReleasesTheStandInTheColourPassBindsWithoutAnAtlas()
    {
        // A scene with no shadow atlas still binds a texture and a sampler on
        // every colour-pass draw, and both of them belong to the device that
        // was lost. Neither is reachable through the atlas path, so neither is
        // released by anything that keys on the atlas alone.
        using var fixture = new ShadowFixture(SilkGraphicsBackend.Vulkan);
        (ShadowTexture standIn, ShadowSampler sampler) = fixture.BindAndCapture();

        fixture.Device.DeviceLossGeneration++;
        (ShadowTexture replacement, ShadowSampler replacementSampler) =
            fixture.BindAndCapture();

        await Assert.That(standIn.Width).IsEqualTo(1u);
        await Assert.That(standIn.IsDisposed).IsTrue();
        await Assert.That(replacement).IsNotEqualTo(standIn);
        await Assert.That(replacement.IsDisposed).IsFalse();
        await Assert.That(replacement.Width).IsEqualTo(1u);
        await Assert.That(sampler.IsDisposed).IsTrue();
        await Assert.That(replacementSampler).IsNotEqualTo(sampler);
        await Assert.That(replacementSampler.IsDisposed).IsFalse();
        await Assert.That(fixture.Device.CreatedStandInTextureCount).IsEqualTo(2);
        await Assert.That(fixture.Device.CreatedSamplers.Count).IsEqualTo(2);
    }

    private sealed class ShadowFixture : IDisposable
    {
        private readonly SilkGraphicsPipelineCache _pipelines;
        private uint _appliedResolution;
        private ulong _revision = 1;

        internal ShadowFixture(
            SilkGraphicsBackend backend = SilkGraphicsBackend.D3D12)
        {
            Device = new ShadowDevice(backend);
            _pipelines = new SilkGraphicsPipelineCache(
                Device,
                backend == SilkGraphicsBackend.Vulkan
                    ? SilkShaderBinaryFormat.SpirV
                    : SilkShaderBinaryFormat.Dxil);
            Cache = new SilkShadowMapCache(Device, _pipelines);
            Resources = new SilkSceneGpuResources(
                Device,
                (_, _) => throw new InvalidOperationException("No image is decoded."));
        }

        internal ShadowDevice Device { get; }

        internal SilkShadowMapCache Cache { get; }

        internal SilkSceneGpuResources Resources { get; }

        internal SilkSceneState Scene { get; } = new();

        internal uint Resolution { get; set; } = 512;

        internal void Prepare()
        {
            if (_appliedResolution != Resolution)
            {
                _ = Scene.Apply(
                    [.. HighShadowFrame(1), .. CreateShadowTable(Resolution)], 2, ++_revision);
                _appliedResolution = Resolution;
            }
            _ = Cache.Prepare(Scene, Resources);
        }

        /// <summary>Binds the cache and returns the texture it bound.</summary>
        internal ShadowTexture BindAndCaptureAtlas() => BindAndCapture().Atlas;

        /// <summary>Binds the cache and returns both objects it bound.</summary>
        internal (ShadowTexture Atlas, ShadowSampler Sampler) BindAndCapture()
        {
            using var commands = new ShadowCommandList(Device);
            Cache.Bind(commands);
            ShadowTexture atlas = commands.BoundAtlas ??
                throw new InvalidOperationException(
                    "The shadow cache bound no atlas texture.");
            ShadowSampler sampler = commands.BoundSampler ??
                throw new InvalidOperationException(
                    "The shadow cache bound no shadow sampler.");
            return (atlas, sampler);
        }

        public void Dispose()
        {
            Cache.Dispose();
            Resources.Dispose();
            _pipelines.Dispose();
            Device.Dispose();
        }

        /// <summary>Builds an ABI v19 shadow table with one orthographic map.</summary>
        private static byte[] CreateShadowTable(uint resolution)
        {
            var bytes = new byte[24 + 288];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Shadow);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 1u);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 1u);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), 0u);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), 0u);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32), resolution);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(36), 1u);
            for (int element = 0; element < 16; element++)
            {
                double identity = element % 5 == 0 ? 1 : 0;
                BinaryPrimitives.WriteDoubleLittleEndian(
                    bytes.AsSpan(40 + (element * 8)),
                    identity);
                BinaryPrimitives.WriteDoubleLittleEndian(
                    bytes.AsSpan(168 + (element * 8)),
                    identity);
            }
            return bytes;
        }
    }

    internal sealed class ShadowDevice(SilkGraphicsBackend backend)
        : ISilkGraphicsDevice,
            ISilkDeviceLossGraphicsDevice,
            ISilkSelectionOutlineGraphicsDevice
    {
        public ulong DeviceLossGeneration { get; set; }

        public ulong SelectionOutlineDeviceGeneration { get; set; }

        public SilkSelectionOutlineCapabilities SelectionOutlineCapabilities =>
            new(false, false);

        /// <summary>Whether the next texture allocation is refused exactly once.</summary>
        internal bool RefuseNextTexture { get; set; }

        /// <summary>Textures allocated at more than one texel.</summary>
        internal int CreatedShadowTextureCount { get; private set; }

        /// <summary>One-texel stand-in textures allocated.</summary>
        internal int CreatedStandInTextureCount { get; private set; }

        /// <summary>Every sampler this device handed out, in creation order.</summary>
        internal List<ShadowSampler> CreatedSamplers { get; } = [];

        public SilkGraphicsBackend Backend => backend;

        public SilkGraphicsCapabilities Capabilities => new(
            "Shadow atlas retention test device",
            "test",
            SupportsCompute: false,
            IsSoftware: true)
        {
            SupportsRasterShadows = true,
        };

        public ISilkGraphicsTexture CreateTexture2D(
            uint width,
            uint height,
            SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
            CreateTexture2D(new SilkTextureDescriptor(
                width,
                height,
                format,
                SilkTextureDescriptor.GetDefaultUsage(format)));

        public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor)
        {
            descriptor.Validate();
            if (RefuseNextTexture)
            {
                RefuseNextTexture = false;
                throw new InvalidOperationException(
                    "This device refuses one texture allocation.");
            }
            if (descriptor.Width > 1)
            {
                CreatedShadowTextureCount++;
            }
            else
            {
                CreatedStandInTextureCount++;
            }
            return new ShadowTexture(descriptor);
        }

        public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage) =>
            new ShadowBuffer(size, usage);

        public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor)
        {
            var sampler = new ShadowSampler(descriptor);
            CreatedSamplers.Add(sampler);
            return sampler;
        }

        public ISilkGraphicsShaderModule CreateShaderModule(
            SilkShaderModuleDescriptor descriptor) => throw new NotSupportedException();

        public ISilkGraphicsBindingLayout CreateBindingLayout(
            SilkBindingLayoutDescriptor descriptor) => throw new NotSupportedException();

        public ISilkGraphicsShaderProgram CreateShaderProgram(
            SilkShaderProgramDescriptor descriptor) => throw new NotSupportedException();

        public ISilkGraphicsPipeline CreateGraphicsPipeline(
            SilkGraphicsPipelineDescriptor descriptor) => throw new NotSupportedException();

        public ISilkComputeBindingLayout CreateComputeBindingLayout(
            SilkComputeBindingLayoutDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputeShaderProgram CreateComputeShaderProgram(
            SilkComputeShaderProgramDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputePipeline CreateComputePipeline(
            SilkComputePipelineDescriptor descriptor) => throw new NotSupportedException();

        public ISilkSelectionMaskGraphicsPipeline CreateSelectionMaskGraphicsPipeline(
            SilkSelectionMaskPipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkSelectionOutlineGraphicsPipeline CreateSelectionOutlineGraphicsPipeline(
            SilkSelectionOutlinePipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkSelectionOutlineBinding CreateSelectionOutlineBinding(
            SilkSelectionOutlineBindingDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsCommandList CreateCommandList() => new ShadowCommandList(this);

        public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList)
        {
            ArgumentNullException.ThrowIfNull(commandList);
            return new ShadowSubmission();
        }

        public void WaitIdle()
        {
        }

        public void Dispose()
        {
        }

        internal Dictionary<float, string> CasterPathsByX { get; } = [];

        internal List<RecordedShadowDraw> RecordedCasterDraws { get; } = [];

        internal List<SilkGraphicsPipelineDescriptor> RecordedPipelines { get; } = [];

        ISilkGraphicsShaderModule ISilkGraphicsDevice.CreateShaderModule(
            SilkShaderModuleDescriptor descriptor) => CasterPathsByX.Count == 0
                ? CreateShaderModule(descriptor)
                : new RecordingShadowModule(descriptor);

        ISilkGraphicsBindingLayout ISilkGraphicsDevice.CreateBindingLayout(
            SilkBindingLayoutDescriptor descriptor) => CasterPathsByX.Count == 0
                ? CreateBindingLayout(descriptor)
                : new RecordingShadowLayout(descriptor);

        ISilkGraphicsShaderProgram ISilkGraphicsDevice.CreateShaderProgram(
            SilkShaderProgramDescriptor descriptor) => CasterPathsByX.Count == 0
                ? CreateShaderProgram(descriptor)
                : new RecordingShadowProgram(descriptor.BindingLayout);

        ISilkGraphicsPipeline ISilkGraphicsDevice.CreateGraphicsPipeline(
            SilkGraphicsPipelineDescriptor descriptor)
        {
            if (CasterPathsByX.Count == 0)
            {
                return CreateGraphicsPipeline(descriptor);
            }
            RecordedPipelines.Add(descriptor);
            return new RecordingShadowPipeline(descriptor);
        }
    }

    internal sealed class ShadowTexture(SilkTextureDescriptor descriptor)
        : ISilkGraphicsTexture
    {
        internal bool IsDisposed { get; private set; }

        public uint Width => descriptor.Width;

        public uint Height => descriptor.Height;

        public SilkTextureFormat Format => descriptor.Format;

        public SilkTextureUsage Usage => descriptor.Usage;

        public uint MipLevelCount => descriptor.MipLevelCount;

        public void ReadbackForTesting(Span<byte> destination) =>
            throw new NotSupportedException();

        public void ReadbackForTesting(Span<float> destination) =>
            throw new NotSupportedException();

        public void Dispose() => IsDisposed = true;
    }

    internal sealed class ShadowSampler(SilkSamplerDescriptor descriptor)
        : ISilkGraphicsSampler
    {
        public SilkSamplerDescriptor Descriptor => descriptor;

        internal bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }

    private sealed class ShadowBuffer(nuint size, SilkBufferUsage usage)
        : ISilkGraphicsBuffer
    {
        private readonly byte[] _bytes = new byte[checked((int)size)];

        public nuint Size => size;

        public SilkBufferUsage Usage => usage;

        public void Write(ReadOnlySpan<byte> data, nuint offset = 0) =>
            data.CopyTo(_bytes.AsSpan(checked((int)offset)));

        public void ReadbackForTesting(Span<byte> destination) =>
            _bytes.AsSpan(0, destination.Length).CopyTo(destination);

        public void Dispose()
        {
        }
    }

    private sealed class ShadowSubmission : ISilkGraphicsSubmission
    {
        public bool IsCompleted => true;

        public void Wait()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class ShadowCommandList(ShadowDevice device)
        : ISilkGraphicsCommandList, ISilkShadowGraphicsCommandList
    {
        /// <summary>The texture bound to the shadow atlas slot, if any.</summary>
        internal ShadowTexture? BoundAtlas { get; private set; }

        /// <summary>The sampler bound to the shadow sampler slot, if any.</summary>
        internal ShadowSampler? BoundSampler { get; private set; }

        public void SetTexture(uint setIndex, uint binding, ISilkGraphicsTexture texture)
        {
            if (binding == SilkBindingLayoutDescriptor.ShadowAtlasTextureBinding)
            {
                BoundAtlas = (ShadowTexture)texture;
            }
        }

        public void BeginShadowRendering(SilkShadowRenderingDescriptor descriptor) =>
            descriptor.Validate();

        public void UploadTexture(ISilkGraphicsTexture texture, ReadOnlySpan<byte> source)
        {
        }

        public void SetSampler(uint setIndex, uint binding, ISilkGraphicsSampler sampler)
        {
            if (binding == SilkBindingLayoutDescriptor.ShadowSamplerBinding)
            {
                BoundSampler = (ShadowSampler)sampler;
            }
        }

        public void ClearColor(ISilkGraphicsTexture texture, SilkColor color)
        {
        }

        public void ClearDepth(ISilkGraphicsTexture texture, float depth)
        {
            if (((ShadowTexture)texture).IsDisposed)
            {
                throw new ObjectDisposedException(
                    nameof(ShadowTexture),
                    "A disposed shadow texture was recorded into a command list.");
            }
            _ = device;
        }

        public void BeginRendering(SilkRenderingDescriptor descriptor)
        {
        }

        public void SetGraphicsPipeline(ISilkGraphicsPipeline pipeline)
        {
        }

        public void SetViewport(SilkViewport viewport)
        {
        }

        public void SetScissor(SilkScissor scissor)
        {
        }

        public void SetVertexBuffer(ISilkGraphicsBuffer buffer)
        {
        }

        public void SetIndexBuffer(ISilkGraphicsBuffer buffer)
        {
        }

        public void SetUniformBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer)
        {
        }

        public void EndRendering()
        {
        }

        public void SetComputePipeline(ISilkComputePipeline pipeline)
        {
        }

        public void SetComputeUniformBuffer(
            uint setIndex,
            uint binding,
            ISilkGraphicsBuffer buffer)
        {
        }

        public void Dispatch(uint elementCount)
        {
        }

        public void BufferBarrier(ISilkGraphicsBuffer buffer)
        {
        }

        public void Dispose()
        {
        }

        private ISilkGraphicsBuffer? _recordedShadowInstances;

        void ISilkGraphicsCommandList.SetStorageBuffer(
            uint setIndex, uint binding, ISilkGraphicsBuffer buffer)
        {
            if (setIndex == 0 && binding == 6)
            {
                _recordedShadowInstances = buffer;
            }
        }

        void ISilkGraphicsCommandList.DrawIndexed(uint indexCount) =>
            RecordShadowDraw(indexCount, 1);

        void ISilkGraphicsCommandList.DrawIndexedInstanced(uint indexCount, uint instanceCount) =>
            RecordShadowDraw(indexCount, instanceCount);

        private void RecordShadowDraw(uint indexCount, uint instanceCount)
        {
            if (device.CasterPathsByX.Count == 0)
            {
                return;
            }
            ISilkGraphicsBuffer instances = _recordedShadowInstances ??
                throw new InvalidOperationException("A recorded shadow draw has no instance buffer.");
            var bytes = new byte[checked((int)instanceCount * 80)];
            instances.ReadbackForTesting(bytes);
            var paths = new string[checked((int)instanceCount)];
            for (int instance = 0; instance < paths.Length; instance++)
            {
                float x = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan((instance * 80) + 12));
                paths[instance] = device.CasterPathsByX[x];
            }
            device.RecordedCasterDraws.Add(new RecordedShadowDraw(indexCount, instanceCount, paths, bytes));
        }
    }

    [Test]
    [Arguments(96)]
    [Arguments(127)]
    public async Task HighShadowMembershipInvalidatesCasterMaps(int index)
    {
        using var fixture = new ShadowFixture();
        const string litPath = "/World/Geom/LitButExcludedCaster";
        const string unlitPath = "/World/Geom/UnlitCaster";
        UInt128 high = UInt128.One << index;
        fixture.Device.CasterPathsByX[-3] = litPath;
        fixture.Device.CasterPathsByX[5] = unlitPath;
        fixture.Resources.Apply(fixture.Scene, fixture.Scene.Apply(
            [
                .. HighShadowFrame(index + 1),
                .. SilkTransactionalApplyTests.Mesh(litPath, 1, x: -3),
                .. SilkTransactionalApplyTests.Mesh(unlitPath, 2, x: 5),
                .. HighShadowLinks((uint)index + 1,
                    (litPath, high | 1, UInt128.One), (unlitPath, UInt128.One, high | 1)),
                .. HighShadowTable((uint)index + 1, (uint)index),
            ], 5, 1));
        IReadOnlyList<SilkShadowDescriptor> descriptors = fixture.Scene.Shadows.Descriptors;
        ulong frameRevision = fixture.Scene.Frame.Revision;
        ulong geometryRevision = fixture.Scene.GeometryRevision;
        ulong shadowRevision = fixture.Scene.Shadows.Revision;
        ulong linkRevision = fixture.Scene.LightLinks.Revision;

        int firstDraws = fixture.Cache.Prepare(fixture.Scene, fixture.Resources);
        ShadowTexture atlas = fixture.BindAndCaptureAtlas();
        await Assert.That(firstDraws).IsEqualTo(1);
        await AssertRecordedCaster(fixture.Device, unlitPath, 5);
        await Assert.That(fixture.Scene.LightLinks.Resolve(unlitPath, 0).IsLit(index)).IsFalse();
        await Assert.That(fixture.Scene.LightLinks.Resolve(unlitPath, 0).CastsShadow(index)).IsTrue();
        await Assert.That(fixture.Scene.LightLinks.Resolve(litPath, 0).IsLit(index)).IsTrue();
        await Assert.That(fixture.Scene.LightLinks.Resolve(litPath, 0).CastsShadow(index)).IsFalse();
        await Assert.That(fixture.Cache.RenderCount).IsEqualTo(1UL);
        await Assert.That(fixture.Cache.MapCount).IsEqualTo(1);
        await Assert.That(fixture.Cache.Binding.GetSlotForLight(index)).IsEqualTo(0);
        await Assert.That(fixture.Device.CreatedShadowTextureCount).IsEqualTo(1);
        await Assert.That(fixture.Device.RecordedPipelines.Single().DepthOnly).IsTrue();

        fixture.Device.RecordedCasterDraws.Clear();
        await Assert.That(fixture.Cache.Prepare(fixture.Scene, fixture.Resources)).IsEqualTo(0);
        await Assert.That(fixture.Device.RecordedCasterDraws.Count).IsEqualTo(0);
        await Assert.That(fixture.Cache.RenderCount).IsEqualTo(1UL);

        // All low words and all illumination masks stay identical. Only the
        // selected high SHADOW bit moves to the other caster.
        fixture.Resources.Apply(fixture.Scene, fixture.Scene.Apply(
            HighShadowLinks((uint)index + 1,
                (litPath, high | 1, high | 1), (unlitPath, UInt128.One, UInt128.One)), 1, 2));
        int changedDraws = fixture.Cache.Prepare(fixture.Scene, fixture.Resources);
        await Assert.That(changedDraws).IsEqualTo(1);
        await AssertRecordedCaster(fixture.Device, litPath, -3);
        await Assert.That(fixture.Scene.LightLinks.Revision).IsEqualTo(linkRevision + 1);
        await Assert.That(fixture.Scene.Frame.Revision).IsEqualTo(frameRevision);
        await Assert.That(fixture.Scene.GeometryRevision).IsEqualTo(geometryRevision);
        await Assert.That(fixture.Scene.Shadows.Revision).IsEqualTo(shadowRevision);
        await Assert.That(fixture.Scene.Shadows.Descriptors).IsSameReferenceAs(descriptors);
        await Assert.That(fixture.Scene.LightLinks.Resolve(litPath, 0).LightMask).IsEqualTo(high | 1);
        await Assert.That(fixture.Scene.LightLinks.Resolve(unlitPath, 0).LightMask).IsEqualTo(UInt128.One);
        await Assert.That(fixture.Scene.LightLinks.Resolve(litPath, 0).CastsShadow(index)).IsTrue();
        await Assert.That(fixture.Scene.LightLinks.Resolve(unlitPath, 0).CastsShadow(index)).IsFalse();
        await Assert.That(fixture.Cache.RenderCount).IsEqualTo(2UL);
        await Assert.That(fixture.Cache.MapCount).IsEqualTo(1);
        await Assert.That(fixture.BindAndCaptureAtlas()).IsSameReferenceAs(atlas);
        await Assert.That(atlas.IsDisposed).IsFalse();
        await Assert.That(fixture.Device.CreatedShadowTextureCount).IsEqualTo(1);

        fixture.Device.RecordedCasterDraws.Clear();
        await Assert.That(fixture.Cache.Prepare(fixture.Scene, fixture.Resources)).IsEqualTo(0);
        await Assert.That(fixture.Device.RecordedCasterDraws.Count).IsEqualTo(0);
        await Assert.That(fixture.Cache.RenderCount).IsEqualTo(2UL);
        await Assert.That(fixture.BindAndCaptureAtlas()).IsSameReferenceAs(atlas);
    }

    [Test]
    public async Task RetiredHighShadowSlotsDoNotRetainCasterState()
    {
        using var fixture = new ShadowFixture();
        const string firstPath = "/World/Geom/Caster127";
        const string secondPath = "/World/Geom/Caster96";
        UInt128 high127 = UInt128.One << 127;
        UInt128 high96 = UInt128.One << 96;
        fixture.Device.CasterPathsByX[-3] = firstPath;
        fixture.Device.CasterPathsByX[5] = secondPath;
        byte[] links = HighShadowLinks(128,
            (firstPath, high96 | 1, high127 | 1), (secondPath, high127 | 1, high96 | 1));
        fixture.Resources.Apply(fixture.Scene, fixture.Scene.Apply(
            [
                .. HighShadowFrame(128),
                .. SilkTransactionalApplyTests.Mesh(firstPath, 1, x: -3),
                .. SilkTransactionalApplyTests.Mesh(secondPath, 2, x: 5),
                .. links, .. HighShadowTable(128, 127),
            ], 5, 1));
        await Assert.That(fixture.Cache.Prepare(fixture.Scene, fixture.Resources)).IsEqualTo(1);
        await AssertRecordedCaster(fixture.Device, firstPath, -3);
        ShadowTexture atlas = fixture.BindAndCaptureAtlas();
        ulong linkRevision = fixture.Scene.LightLinks.Revision;

        fixture.Device.RecordedCasterDraws.Clear();
        _ = fixture.Scene.Apply(HighShadowTable(128, 96), 1, 2);
        await Assert.That(fixture.Cache.Prepare(fixture.Scene, fixture.Resources)).IsEqualTo(1);
        await AssertRecordedCaster(fixture.Device, secondPath, 5);
        await Assert.That(fixture.Cache.RenderCount).IsEqualTo(2UL);
        await Assert.That(fixture.Cache.MapCount).IsEqualTo(1);
        await Assert.That(fixture.Scene.LightLinks.Revision).IsEqualTo(linkRevision);
        await Assert.That(fixture.Cache.Binding.GetSlotForLight(127)).IsEqualTo(-1);
        await Assert.That(fixture.Cache.Binding.GetSlotForLight(96)).IsEqualTo(0);
        await Assert.That(fixture.BindAndCaptureAtlas()).IsSameReferenceAs(atlas);
        await Assert.That(fixture.Device.CreatedShadowTextureCount).IsEqualTo(1);
        ISilkGraphicsBuffer frame = fixture.Resources.RequireFrameBuffer(
            fixture.Scene, RenderOutputTransform.Identity, 0, fixture.Cache.Binding, fixture.Cache.BindingRevision);
        var bytes = new byte[15296];
        frame.ReadbackForTesting(bytes);
        await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(12960 + (127 * 16))))
            .IsEqualTo(-1f);
        await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(12960 + (96 * 16))))
            .IsEqualTo(0f);

        fixture.Device.RecordedCasterDraws.Clear();
        fixture.Resources.Apply(fixture.Scene, fixture.Scene.Apply(
            [
                .. HighShadowTable(0), .. HighShadowLinks(0), .. HighShadowFrame(0),
                .. HighShadowMeshRemoval(firstPath), .. HighShadowMeshRemoval(secondPath),
            ], 5, 3));
        await Assert.That(fixture.Cache.Prepare(fixture.Scene, fixture.Resources)).IsEqualTo(0);
        await Assert.That(fixture.Cache.MapCount).IsEqualTo(0);
        await Assert.That(fixture.Cache.RenderCount).IsEqualTo(2UL);
        await Assert.That(fixture.Device.RecordedCasterDraws.Count).IsEqualTo(0);
        await Assert.That(fixture.Scene.Meshes.Count).IsEqualTo(0);
        await Assert.That(fixture.Resources.MeshValues.Count).IsEqualTo(0);
        await Assert.That(fixture.Scene.Shadows.Count).IsEqualTo(0);
        await Assert.That(fixture.Cache.Binding.GetSlotForLight(96)).IsEqualTo(-1);
        await Assert.That(fixture.Cache.Binding.GetSlotForLight(127)).IsEqualTo(-1);
        ShadowTexture standIn = fixture.BindAndCaptureAtlas();
        await Assert.That(standIn.Width).IsEqualTo(1u);
        await Assert.That(ReferenceEquals(standIn, atlas)).IsFalse();
        await Assert.That(atlas.IsDisposed).IsTrue();
        await Assert.That(fixture.Device.CreatedStandInTextureCount).IsEqualTo(1);
        ISilkGraphicsBuffer retiredFrame = fixture.Resources.RequireFrameBuffer(
            fixture.Scene, RenderOutputTransform.Identity, 0, fixture.Cache.Binding, fixture.Cache.BindingRevision);
        await Assert.That(retiredFrame).IsSameReferenceAs(frame);
        retiredFrame.ReadbackForTesting(bytes);
        for (int index = 0; index < 128; index++)
        {
            await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(12960 + (index * 16))))
                .IsEqualTo(-1f);
        }

        // Republish a different high map after the empty scene. No old caster
        // or slot may be resurrected with the same descriptor resolution.
        fixture.Resources.Apply(fixture.Scene, fixture.Scene.Apply(
            [
                .. HighShadowFrame(97),
                .. SilkTransactionalApplyTests.Mesh(secondPath, 2, x: 5),
                .. HighShadowLinks(97, (secondPath, UInt128.One, high96 | 1)),
                .. HighShadowTable(97, 96),
            ], 4, 4));
        await Assert.That(fixture.Cache.Prepare(fixture.Scene, fixture.Resources)).IsEqualTo(1);
        await AssertRecordedCaster(fixture.Device, secondPath, 5);
        await Assert.That(fixture.Cache.RenderCount).IsEqualTo(3UL);
        await Assert.That(fixture.Cache.MapCount).IsEqualTo(1);
        await Assert.That(fixture.Cache.Binding.GetSlotForLight(127)).IsEqualTo(-1);
        await Assert.That(fixture.Cache.Binding.GetSlotForLight(96)).IsEqualTo(0);
        await Assert.That(fixture.Device.CreatedShadowTextureCount).IsEqualTo(2);
        ShadowTexture rebound = fixture.BindAndCaptureAtlas();
        await Assert.That(ReferenceEquals(rebound, atlas)).IsFalse();
        await Assert.That(rebound.IsDisposed).IsFalse();
    }

    private static async Task AssertRecordedCaster(ShadowDevice device, string path, float x)
    {
        await Assert.That(device.RecordedCasterDraws.Count).IsEqualTo(1);
        RecordedShadowDraw draw = device.RecordedCasterDraws.Single();
        await Assert.That(draw.IndexCount).IsEqualTo(3u);
        await Assert.That(draw.InstanceCount).IsEqualTo(1u);
        await Assert.That(draw.Paths.Single()).IsEqualTo(path);
        float[] expected =
        [
            1, 0, 0, x, 0, 1, 0, 0, 0, 0, 0.5f, 0.5f, 0, 0, 0, 1,
            0, 0, 0, 0,
        ];
        await Assert.That(draw.Bytes.Length).IsEqualTo(80);
        for (int index = 0; index < expected.Length; index++)
        {
            await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(draw.Bytes.AsSpan(index * 4)))
                .IsEqualTo(expected[index]);
        }
    }

    private static byte[] HighShadowFrame(int count)
    {
        var bytes = new byte[23368];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 23368u);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 64);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 64);
        for (int element = 0; element < 16; element++)
        {
            double identity = element % 5 == 0 ? 1 : 0;
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(16 + (element * 8)), identity);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(144 + (element * 8)), identity);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(536), (uint)count);
        for (int light = 0; light < count; light++)
        {
            int entry = 552 + (light * 176);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry), 1u);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 4),
                light is 96 or 127 ? 1u : 0u);
            for (int component = 0; component < 4; component++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 16 + (component * 4)), 1f);
            }
            for (int element = 0; element < 16; element++)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(
                    bytes.AsSpan(entry + 32 + (element * 8)), element % 5 == 0 ? 1 : 0);
            }
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 164), 1f);
        }
        return bytes;
    }

    private static byte[] HighShadowLinks(
        uint count, params (string Path, UInt128 Light, UInt128 Shadow)[] entries)
    {
        var bytes = new byte[24 + entries.Sum(entry => 44 + System.Text.Encoding.UTF8.GetByteCount(entry.Path))];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.LightLink);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)entries.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), count);
        int offset = 24;
        foreach ((string path, UInt128 light, UInt128 shadow) in entries)
        {
            byte[] pathBytes = System.Text.Encoding.UTF8.GetBytes(path);
            BinaryPrimitives.WriteUInt128LittleEndian(bytes.AsSpan(offset), light);
            BinaryPrimitives.WriteUInt128LittleEndian(bytes.AsSpan(offset + 16), shadow);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 36), -1);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 40), (uint)pathBytes.Length);
            pathBytes.CopyTo(bytes, offset + 44);
            offset += 44 + pathBytes.Length;
        }
        return bytes;
    }

    private static byte[] HighShadowTable(uint count, params uint[] indices)
    {
        var bytes = new byte[24 + (indices.Length * 288)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Shadow);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)indices.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), count);
        for (int slot = 0; slot < indices.Length; slot++)
        {
            int entry = 24 + (slot * 288);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry), indices[slot]);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 4), (uint)slot);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 8), 512u);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 12), 3u);
            for (int element = 0; element < 16; element++)
            {
                double identity = element % 5 == 0 ? 1 : 0;
                BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(entry + 16 + (element * 8)), identity);
                BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(entry + 144 + (element * 8)), identity);
            }
        }
        return bytes;
    }

    private static byte[] HighShadowMeshRemoval(string path)
    {
        byte[] pathBytes = System.Text.Encoding.UTF8.GetBytes(path);
        var bytes = new byte[24 + pathBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshRemove);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8),
            SilkEnvironmentLightingTests.ComputeStableHash(path));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), (uint)pathBytes.Length);
        pathBytes.CopyTo(bytes, 24);
        return bytes;
    }

    internal sealed record RecordedShadowDraw(uint IndexCount, uint InstanceCount, string[] Paths, byte[] Bytes);

    private sealed class RecordingShadowModule(SilkShaderModuleDescriptor descriptor) : ISilkGraphicsShaderModule
    {
        public SilkShaderModuleDescriptor Descriptor => descriptor;
        public void Dispose() { }
    }

    private sealed class RecordingShadowLayout(SilkBindingLayoutDescriptor descriptor) : ISilkGraphicsBindingLayout
    {
        public SilkBindingLayoutDescriptor Descriptor => descriptor;
        public void Dispose() { }
    }

    private sealed class RecordingShadowProgram(ISilkGraphicsBindingLayout layout) : ISilkGraphicsShaderProgram
    {
        public ISilkGraphicsBindingLayout BindingLayout => layout;
        public void Dispose() { }
    }

    private sealed class RecordingShadowPipeline(SilkGraphicsPipelineDescriptor descriptor) : ISilkGraphicsPipeline
    {
        public SilkGraphicsPipelineDescriptor Descriptor => descriptor;
        public void Dispose() { }
    }
}
