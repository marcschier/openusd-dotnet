// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Rendering.Tests;

public sealed class StormAovRequestTests
{
    [Test]
    public async Task RequestOwnsItsOrderedOutputsWithoutArrayOrSyncRootEscape()
    {
        StormAovKind[] supplied = [StormAovKind.Color, StormAovKind.Depth];
        var request = new StormAovRequest(64, 64, 0, supplied);
        supplied[0] = StormAovKind.Normal;

        await Assert.That(request.Outputs[0]).IsEqualTo(StormAovKind.Color);
        await Assert.That(request.Outputs[1]).IsEqualTo(StormAovKind.Depth);
        await Assert.That(request.Outputs is StormAovKind[]).IsFalse();
        await Assert.That(request.Outputs is ICollection).IsFalse();
        await Assert.That(request.Outputs is IList<StormAovKind>).IsFalse();
        await Assert.That(request.Width).IsEqualTo(64);
        await Assert.That(request.Height).IsEqualTo(64);
    }

    [Test]
    public async Task CameraInputAndReturnedCopiesCannotMutateRequestState()
    {
        var camera = new CameraState(Matrix4x4.Identity, Matrix4x4.Identity, [Vector4.UnitX]);
        var request = new StormAovRequest(1, 1, 0, [StormAovKind.Depth], camera);
        Vector4[] original = ImmutableCollectionsMarshal.AsArray(
            (ImmutableArray<Vector4>)camera.ClipPlanes)!;
        original[0] = Vector4.UnitY;
        CameraState returned = request.Camera;
        Vector4[] returnedArray = ImmutableCollectionsMarshal.AsArray(
            (ImmutableArray<Vector4>)returned.ClipPlanes)!;
        returnedArray[0] = Vector4.UnitZ;
        await Assert.That(request.Camera.ClipPlanes[0]).IsEqualTo(Vector4.UnitX);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    public async Task InvalidRequestsAreRefusedWithoutNativeWork(int mutation)
    {
        bool refused = false;
        try
        {
            _ = mutation switch
            {
                0 => new StormAovRequest(0, 1, 0, [StormAovKind.Depth]),
                1 => new StormAovRequest(int.MaxValue, 1, 0, [StormAovKind.Depth]),
                2 => new StormAovRequest(1024, 1025, 0, [StormAovKind.Depth]),
                3 => new StormAovRequest(1, 1, 0, [StormAovKind.None]),
                4 => new StormAovRequest(1, 1, 0, [StormAovKind.Color, StormAovKind.Color]),
                5 => new StormAovRequest(1, 1, 0, [StormAovKind.Depth], timeCode: double.NaN),
                6 => new StormAovRequest(1, 1, 0, [StormAovKind.Depth], includeIdentities: true),
                7 => new StormAovRequest(1, 1, 0, [StormAovKind.Depth],
                    limits: new StormAovLimits(managedByteLimit: ulong.MaxValue)),
                _ => throw new InvalidOperationException(),
            };
        }
        catch (ArgumentException)
        {
            refused = true;
        }
        await Assert.That(refused).IsTrue();
    }
}
