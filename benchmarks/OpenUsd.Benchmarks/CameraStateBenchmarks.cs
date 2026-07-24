// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using BenchmarkDotNet.Attributes;
using OpenUsd.Rendering;

namespace OpenUsd.Benchmarks;

[MemoryDiagnoser]
public class CameraStateBenchmarks
{
    private readonly Vector3 _eye = new(8.5f, 5.25f, 11.75f);
    private readonly Vector3 _target = new(0.5f, 1.25f, -0.75f);
    private readonly Vector3 _up = Vector3.UnitY;
    private CameraState _camera;
    private Matrix4x4 _projection;
    private StageRenderState _state = StageRenderState.Default;
    private Matrix4x4 _view;

    [GlobalSetup]
    public void Setup()
    {
        _view = Matrix4x4.CreateLookAt(_eye, _target, _up);
        _projection = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4,
            16f / 9f,
            0.1f,
            2_000f);
        _camera = new CameraState(_view, _projection);
        _state = StageRenderState.Create(new StageIdentity("benchmark-stage"));
    }

    [Benchmark]
    public CameraState BuildProjectionCamera()
    {
        Matrix4x4 view = Matrix4x4.CreateLookAt(_eye, _target, _up);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4,
            16f / 9f,
            0.1f,
            2_000f);
        return new CameraState(view, projection);
    }

    [Benchmark]
    public Matrix4x4 ComposeViewProjection() => _view * _projection;

    [Benchmark]
    public StageRenderState UpdateRenderStateCamera() =>
        _state.WithCamera(_camera);
}
