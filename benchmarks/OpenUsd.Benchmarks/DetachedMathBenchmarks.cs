// Copyright (c) marcschier. Licensed under the MIT License.

using BenchmarkDotNet.Attributes;

namespace OpenUsd.Benchmarks;

[MemoryDiagnoser]
public class DetachedPointTransformBenchmarks
{
    private UsdMatrix4d _matrix;
    private UsdVec3d[] _points = [];

    [Params(16, 256)]
    public int PointCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _matrix = new UsdMatrix4d(
            0.8660254, 0, -0.5, 0,
            0.25, 0.8660254, 0.4330127, 0,
            0.4330127, -0.5, 0.75, 0,
            12.5, -3.25, 7.75, 1);
        _points = new UsdVec3d[PointCount];
        for (int index = 0; index < _points.Length; index++)
        {
            _points[index] = new UsdVec3d(
                index * 0.125,
                (index % 11) - 5,
                (index % 7) * -0.25);
        }
    }

    [Benchmark]
    public UsdVec3d TransformPointBatch()
    {
        double x = 0;
        double y = 0;
        double z = 0;
        foreach (UsdVec3d point in _points)
        {
            UsdVec3d transformed = _matrix.TransformPoint(point);
            x += transformed.X;
            y += transformed.Y;
            z += transformed.Z;
        }
        return new UsdVec3d(x, y, z);
    }
}

[MemoryDiagnoser]
public class DetachedMatrixBenchmarks
{
    private UsdMatrix4d _matrix;

    [GlobalSetup]
    public void Setup()
    {
        _matrix = new UsdMatrix4d(
            1.5, 0.1, 0.2, 0,
            -0.2, 0.75, 0.3, 0,
            0.05, -0.1, 2.25, 0,
            12, -4, 8, 1);
    }

    [Benchmark]
    public UsdMatrix4d InvertMatrix()
    {
        if (!_matrix.TryInvert(out UsdMatrix4d inverse))
        {
            throw new InvalidOperationException("The benchmark matrix must be invertible.");
        }
        return inverse;
    }
}
