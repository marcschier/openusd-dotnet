// Copyright (c) marcschier. Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using OpenUsd.Rendering;

namespace OpenUsd.Benchmarks;

[MemoryDiagnoser]
public class ManagedStateBenchmarks
{
    private RenderPickRequest _pickRequest;
    private StageRenderState _state = StageRenderState.Default;

    [GlobalSetup]
    public void Setup()
    {
        _state = StageRenderState.Create(new StageIdentity("state-benchmark"))
            .WithViewport(new ViewportDimensions(1920, 1080))
            .WithTime(new StageTime(48));
        _pickRequest = new RenderPickRequest(
            960,
            540,
            _state.Viewport,
            _state.Revision,
            requestedSceneRevision: 72,
            target: RenderPickTarget.Face);
    }

    [Benchmark]
    public StageRenderState AdvanceRenderStateRevision() =>
        _state.AdvanceRevision();

    [Benchmark]
    public RenderPickStaleReason InferPickStaleness() =>
        _pickRequest.InferStaleReasons(_state.Revision, 73);
}
