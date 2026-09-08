// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Rendering.Tests;

public sealed class StormAovDecoderTests
{
    [Test]
    public async Task DepthSnapshotIsTypedDetachedAndBoundToExplicitCallerClaims()
    {
        StormAovSnapshot snapshot = DecodeDepth();
        StormAovOutput<float> depth = snapshot.GetOutput<float>(StormAovKind.Depth);
        await Assert.That(depth.Pixels[0]).IsEqualTo(0.25f);
        await Assert.That(depth.Pixels[1]).IsEqualTo(1.0f);
        await Assert.That(depth.Pixels is ICollection).IsFalse();
        await Assert.That(snapshot.Outputs is ICollection).IsFalse();
        await Assert.That(snapshot.AppliedCamera.View is ICollection).IsFalse();
        await Assert.That(snapshot.AppliedCamera.View[0]).IsEqualTo(1.0);
        await Assert.That(snapshot.CallerStateRevision).IsEqualTo(42ul);
        await Assert.That(snapshot.CallerSceneRevision).IsEqualTo(7ul);
        await Assert.That(snapshot.CaptureSequence).IsEqualTo(5ul);
        await Assert.That(depth.DepthConvention).IsEqualTo(StormAovDepthConvention.OpenGlWindow);
    }

    [Test]
    public async Task IdentityTablePreservesCanonicalPathsNativeContextOrderAndDistinctOrdinals()
    {
        StormAovSnapshot snapshot = DecodeIdentity();
        StormAovIdentity identity = snapshot.GetIdentity(0, 0)!;
        await Assert.That(identity.Status).IsEqualTo(StormAovIdentityStatus.Resolved);
        await Assert.That(identity.RawPrimId).IsEqualTo(17);
        await Assert.That(identity.RawInstanceId).IsEqualTo(8);
        await Assert.That(identity.DecodedInstanceIndex).IsEqualTo(8);
        await Assert.That(identity.PrimPath).IsEqualTo("/World/Outer/Inner/Prototype");
        await Assert.That(identity.InstancerPath).IsEqualTo("/World/Outer/Inner");
        await Assert.That(identity.InstancerContext[0].InstancerPath).IsEqualTo("/World/Outer");
        await Assert.That(identity.InstancerContext[0].InstanceIndex).IsEqualTo(4);
        await Assert.That(identity.InstancerContext[1].InstanceIndex).IsEqualTo(2);
        await Assert.That(identity.InstancerContext is ICollection).IsFalse();
        await Assert.That(snapshot.Identities is ICollection).IsFalse();
        await Assert.That(snapshot.IdentityIndices is ICollection).IsFalse();
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
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(11)]
    [Arguments(12)]
    [Arguments(13)]
    [Arguments(14)]
    [Arguments(15)]
    public async Task MalformedIdentityAndUtf8AreRejectedBeforePayloadAllocation(int mutation)
    {
        bool refused = false;
        long before = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            _ = DecodeIdentity(mutation);
        }
        catch (OpenUsdStormException)
        {
            refused = true;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(refused).IsTrue();
        await Assert.That(allocated < 32_768).IsTrue();
    }

    private static unsafe StormAovSnapshot DecodeIdentity(int mutation = -1)
    {
        const string prim = "/World/Outer/Inner/Prototype";
        const string instancer = "/World/Outer/Inner";
        const string outer = "/World/Outer";
        byte[] text = Encoding.UTF8.GetBytes(prim + instancer + outer + instancer);
        var request = new StormAovRequest(
            1, 1, 0, [StormAovKind.PrimId, StormAovKind.InstanceId],
            new CameraState(Matrix4x4.Identity, Matrix4x4.Identity),
            includeIdentities: true);
        ulong* pixels = stackalloc ulong[2] { 17, 8 };
        StormAovNative.Output* outputs = stackalloc StormAovNative.Output[2];
        for (int index = 0; index < 2; index++)
        {
            outputs[index] = new StormAovNative.Output
            {
                StructSize = (uint)Unsafe.SizeOf<StormAovNative.Output>(),
                Version = 1,
                Kind = index == 0 ? StormAovKind.PrimId : StormAovKind.InstanceId,
                Status = StormAovStatus.Ready,
                Format = StormAovFormat.Int32,
                Width = 1,
                Height = 1,
                RowStrideBytes = 4,
                Origin = StormAovOrigin.TopLeft,
                DataOffset = (ulong)index * 8,
                DataBytes = 4,
            };
        }
        StormAovNative.Identity identity = new()
        {
            StructSize = (uint)Unsafe.SizeOf<StormAovNative.Identity>(),
            Version = 1,
            Status = 1,
            PrimId = 17,
            InstanceId = 8,
            InstanceIndex = 8,
            PrimPathLength = (uint)prim.Length,
            InstancerPathOffset = (uint)prim.Length,
            InstancerPathLength = (uint)instancer.Length,
            ContextCount = 2,
        };
        StormAovNative.InstanceContext* contexts = stackalloc StormAovNative.InstanceContext[2];
        contexts[0] = new StormAovNative.InstanceContext
        {
            StructSize = (uint)Unsafe.SizeOf<StormAovNative.InstanceContext>(),
            Version = 1,
            PathOffset = (uint)(prim.Length + instancer.Length),
            PathLength = (uint)outer.Length,
            InstanceIndex = 4,
        };
        contexts[1] = new StormAovNative.InstanceContext
        {
            StructSize = (uint)Unsafe.SizeOf<StormAovNative.InstanceContext>(),
            Version = 1,
            PathOffset = (uint)(prim.Length + instancer.Length + outer.Length),
            PathLength = (uint)instancer.Length,
            InstanceIndex = 2,
        };
        uint identityIndex = 0;
        fixed (byte* textPointer = text)
        {
            StormAovNative.View view = new()
            {
                StructSize = (uint)Unsafe.SizeOf<StormAovNative.View>(),
                Version = 1,
                OutputCount = 2,
                Width = 1,
                Height = 1,
                CaptureId = 1,
                OwnedBytes = 8192,
                AdmittedWorkingBytes = 16384,
                Outputs = outputs,
                PixelData = pixels,
                PixelBytes = 16,
                IdentityStatus = StormAovStatus.Ready,
                Identities = &identity,
                IdentityCount = 1,
                Contexts = contexts,
                ContextCount = 2,
                Text = textPointer,
                TextByteCount = (uint)text.Length,
                IdentityIndices = &identityIndex,
                IdentityIndexCount = 1,
                AppliedCamera = request.NativeCamera,
            };
            switch (mutation)
            {
                case 0:
                    view.IdentityCount = uint.MaxValue;
                    break;
                case 1:
                    view.ContextCount = uint.MaxValue;
                    break;
                case 2:
                    view.TextByteCount = uint.MaxValue;
                    break;
                case 3:
                    view.IdentityIndexCount = ulong.MaxValue;
                    break;
                case 4:
                    identity.StructSize--;
                    break;
                case 5:
                    identity.Version = 999;
                    break;
                case 6:
                    identity.Status = 999;
                    break;
                case 7:
                    identity.PrimPathOffset = uint.MaxValue;
                    break;
                case 8:
                    identity.ContextOffset = uint.MaxValue;
                    break;
                case 9:
                    contexts[0].PathLength = uint.MaxValue;
                    break;
                case 10:
                    contexts[0].InstanceIndex = -1;
                    break;
                case 11:
                    identityIndex = uint.MaxValue;
                    break;
                case 12:
                    identity.PrimId = 18;
                    break;
                case 13:
                    textPointer[1] = 0xff;
                    break;
                case 14:
                    textPointer[0] = (byte)'x';
                    break;
                case 15:
                    identity.Status = 2;
                    break;
            }
            return StormAovDecoder.Decode(in view, request);
        }
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
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(11)]
    [Arguments(12)]
    [Arguments(13)]
    [Arguments(14)]
    [Arguments(15)]
    [Arguments(16)]
    [Arguments(17)]
    [Arguments(18)]
    [Arguments(19)]
    [Arguments(20)]
    [Arguments(21)]
    [Arguments(22)]
    [Arguments(23)]
    [Arguments(24)]
    [Arguments(25)]
    [Arguments(26)]
    [Arguments(27)]
    public async Task MalformedViewOrPixelsAreRejectedBeforePayloadAllocation(int mutation)
    {
        (bool refused, long allocated) = RejectMalformedDepth(mutation);
        await Assert.That(refused).IsTrue();
        await Assert.That(allocated < 32_768).IsTrue();
    }

    private static unsafe (bool Refused, long Allocated) RejectMalformedDepth(int mutation)
    {
        var request = new StormAovRequest(
            2, 1, 0, [StormAovKind.Depth],
            new CameraState(Matrix4x4.Identity, Matrix4x4.Identity),
            limits: mutation == 27 ? new StormAovLimits(managedByteLimit: 8192) : null);
        float* pixels = stackalloc float[2] { 0.25f, 1.0f };
        StormAovNative.Output output = new()
        {
            StructSize = (uint)Unsafe.SizeOf<StormAovNative.Output>(),
            Version = 1,
            Kind = StormAovKind.Depth,
            Status = StormAovStatus.Ready,
            Format = StormAovFormat.Float32,
            Width = 2,
            Height = 1,
            RowStrideBytes = 8,
            Origin = StormAovOrigin.TopLeft,
            DepthConvention = StormAovDepthConvention.OpenGlWindow,
            DataBytes = 8,
        };
        StormAovNative.View view = new()
        {
            StructSize = (uint)Unsafe.SizeOf<StormAovNative.View>(),
            Version = 1,
            OutputCount = 1,
            Width = 2,
            Height = 1,
            CaptureId = 1,
            OwnedBytes = 4096,
            AdmittedWorkingBytes = 8192,
            Outputs = &output,
            PixelData = pixels,
            PixelBytes = 8,
            AppliedCamera = request.NativeCamera,
        };
        switch (mutation)
        {
            case 0:
                view.StructSize--;
                break;
            case 1:
                view.Version = 999;
                break;
            case 2:
                view.OutputCount = uint.MaxValue;
                break;
            case 3:
                view.Width = uint.MaxValue;
                break;
            case 4:
                view.Height = 0;
                break;
            case 5:
                view.CaptureId = 0;
                break;
            case 6:
                view.RevisionFlags = uint.MaxValue;
                break;
            case 7:
                view.TimeCode = double.NaN;
                break;
            case 8:
                view.StateRevision = 1;
                break;
            case 9:
                view.SceneRevision = 1;
                break;
            case 10:
                view.OwnedBytes = ulong.MaxValue;
                break;
            case 11:
                view.AdmittedWorkingBytes = ulong.MaxValue;
                break;
            case 12:
                view.RetainedScratchUpperBoundBytes = ulong.MaxValue;
                break;
            case 13:
                view.Outputs = null;
                break;
            case 14:
                view.PixelData = (void*)(nuint.MaxValue - 7);
                break;
            case 15:
                view.PixelBytes = ulong.MaxValue;
                break;
            case 16:
                output.StructSize--;
                break;
            case 17:
                output.Kind = StormAovKind.Color;
                break;
            case 18:
                output.Status = StormAovStatus.NotRequested;
                break;
            case 19:
                output.Format = StormAovFormat.Int32;
                break;
            case 20:
                output.RowStrideBytes = 16;
                break;
            case 21:
                output.Origin = (StormAovOrigin)2;
                break;
            case 22:
                output.DataOffset = 8;
                break;
            case 23:
                output.DataBytes = 4;
                break;
            case 24:
                pixels[0] = float.NaN;
                break;
            case 25:
                pixels[0] = -0.1f;
                break;
            case 26:
                pixels[0] = 1.1f;
                break;
        }
        bool refused = false;
        long before = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            _ = StormAovDecoder.Decode(in view, request);
        }
        catch (OpenUsdStormException)
        {
            refused = true;
        }
        return (refused, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static unsafe StormAovSnapshot DecodeDepth()
    {
        var request = new StormAovRequest(
            2, 1, 0, [StormAovKind.Depth],
            new CameraState(Matrix4x4.Identity, Matrix4x4.Identity),
            callerStateRevision: 42, callerSceneRevision: 7);
        float* pixels = stackalloc float[2] { 0.25f, 1.0f };
        StormAovNative.Output output = new()
        {
            StructSize = (uint)Unsafe.SizeOf<StormAovNative.Output>(),
            Version = 1,
            Kind = StormAovKind.Depth,
            Status = StormAovStatus.Ready,
            Format = StormAovFormat.Float32,
            Width = 2,
            Height = 1,
            RowStrideBytes = 8,
            Origin = StormAovOrigin.TopLeft,
            DepthConvention = StormAovDepthConvention.OpenGlWindow,
            DataBytes = 8,
        };
        StormAovNative.View view = new()
        {
            StructSize = (uint)Unsafe.SizeOf<StormAovNative.View>(),
            Version = 1,
            OutputCount = 1,
            Width = 2,
            Height = 1,
            RevisionFlags = 1,
            CaptureId = 5,
            StateRevision = 42,
            SceneRevision = 7,
            OwnedBytes = 4096,
            AdmittedWorkingBytes = 8192,
            Outputs = &output,
            PixelData = pixels,
            PixelBytes = 8,
            AppliedCamera = request.NativeCamera,
        };
        StormAovSnapshot result = StormAovDecoder.Decode(in view, request);
        pixels[0] = 0.875f;
        return result;
    }
}
