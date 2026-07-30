// Copyright (c) marcschier. Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Benchmarks;

[MemoryDiagnoser]
public class SilkGpuInstanceBenchmarks
{
    private byte[] _page = [];
    private uint _commandCount;

    [Params(1024)]
    public int InstanceCount { get; set; }

    [Params(true, false)]
    public bool SharedPrototypePath { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var commands = new byte[checked(InstanceCount + 1)][];
        commands[0] = SilkBenchmarkData.CreateFrameCommand();
        for (int index = 0; index < InstanceCount; index++)
        {
            commands[index + 1] = SilkBenchmarkData.CreateMeshCommand(
                SharedPrototypePath ? "/World/Prototype" : SilkBenchmarkData.GetMeshPath(index),
                primId: SharedPrototypePath ? 42 : index + 1,
                triangleCount: 8,
                instanceIndex: SharedPrototypePath ? index : 0);
        }
        _page = SilkBenchmarkData.Concat(commands);
        _commandCount = checked((uint)commands.Length);
    }

    [Benchmark]
    [BenchmarkCategory("Smoke")]
    public SilkSceneGpuStatistics ApplyGpuResources()
    {
        var scene = new SilkSceneState();
        SilkSceneDelta delta = scene.Apply(_page, _commandCount, revision: 1);
        using var device = new BenchmarkGraphicsDevice();
        using var resources = new SilkSceneGpuResources(device);
        resources.Apply(scene, delta);
        return resources.Statistics;
    }

    private sealed class BenchmarkGraphicsDevice : ISilkGraphicsDevice
    {
        public SilkGraphicsBackend Backend => SilkGraphicsBackend.Vulkan;

        public SilkGraphicsCapabilities Capabilities { get; } =
            new("Benchmark", "1", SupportsCompute: false, IsSoftware: true);

        public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage) =>
            new BenchmarkGraphicsBuffer(size, usage);

        public ISilkGraphicsTexture CreateTexture2D(
            uint width,
            uint height,
            SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
            throw new NotSupportedException();

        public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsShaderModule CreateShaderModule(SilkShaderModuleDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsBindingLayout CreateBindingLayout(SilkBindingLayoutDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsShaderProgram CreateShaderProgram(SilkShaderProgramDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsPipeline CreateGraphicsPipeline(SilkGraphicsPipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputeBindingLayout CreateComputeBindingLayout(
            SilkComputeBindingLayoutDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputeShaderProgram CreateComputeShaderProgram(
            SilkComputeShaderProgramDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputePipeline CreateComputePipeline(SilkComputePipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsCommandList CreateCommandList() =>
            throw new NotSupportedException();

        public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList) =>
            throw new NotSupportedException();

        public void WaitIdle()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class BenchmarkGraphicsBuffer(nuint size, SilkBufferUsage usage)
        : SilkGraphicsBufferBase(size, usage)
    {
        public override void Write(ReadOnlySpan<byte> data, nuint offset = 0)
        {
            _ = ValidateWrite(data.Length, offset);
        }

        public override void ReadbackForTesting(Span<byte> destination)
        {
            _ = ValidateReadback(destination.Length);
            destination.Clear();
        }

        protected override void ReleaseNative()
        {
        }
    }
}
