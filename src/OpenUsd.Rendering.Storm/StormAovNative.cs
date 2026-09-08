// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OpenUsd.Rendering.Storm;

internal static unsafe class StormAovNative
{
    internal const uint Version = 1;
    internal const uint IncludeIdentities = 1;
    internal const uint HasSceneRevision = 1;
    internal const uint UseSceneLights = 2;
    internal const uint ResolvedMultisample = 1;
    internal const uint BackgroundIndex = uint.MaxValue;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Request
    {
        internal uint StructSize;
        internal uint Version;
        internal uint OutputCount;
        internal uint Flags;
        internal int Width;
        internal int Height;
        internal uint Framebuffer;
        internal uint RevisionFlags;
        internal double TimeCode;
        internal ulong StateRevision;
        internal ulong SceneRevision;
        internal uint MaxPixels;
        internal uint MaxUniqueIdentities;
        internal uint MaxInstanceContexts;
        internal uint MaxTextBytes;
        internal ulong MaxWorkingBytes;
        internal fixed uint OutputKinds[StormAovLimits.MaximumOutputs];
        internal NativeRenderCamera Camera;

        internal static Request Create(StormAovRequest request)
        {
            var result = new Request
            {
                StructSize = (uint)Unsafe.SizeOf<Request>(),
                Version = StormAovNative.Version,
                OutputCount = (uint)request.Outputs.Count,
                Flags = request.IncludeIdentities ? IncludeIdentities : 0,
                Width = request.Width,
                Height = request.Height,
                Framebuffer = request.Framebuffer,
                RevisionFlags =
                    (request.CallerSceneRevision.HasValue ? HasSceneRevision : 0) |
                    (request.UseSceneLights ? UseSceneLights : 0),
                TimeCode = request.TimeCode,
                StateRevision = request.CallerStateRevision,
                SceneRevision = request.CallerSceneRevision.GetValueOrDefault(),
                MaxPixels = (uint)request.Limits.PixelLimit,
                MaxUniqueIdentities = request.IncludeIdentities ? (uint)request.Limits.IdentityLimit : 0,
                MaxInstanceContexts = request.IncludeIdentities ? (uint)request.Limits.InstanceContextLimit : 0,
                MaxTextBytes = request.IncludeIdentities ? (uint)request.Limits.PathByteLimit : 0,
                MaxWorkingBytes = request.Limits.NativeWorkingByteLimit,
                Camera = request.NativeCamera,
            };
            for (int index = 0; index < request.Outputs.Count; index++)
            {
                result.OutputKinds[index] = (uint)request.Outputs[index];
            }
            return result;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Output
    {
        internal uint StructSize;
        internal uint Version;
        internal StormAovKind Kind;
        internal StormAovStatus Status;
        internal StormAovFormat Format;
        internal uint Width;
        internal uint Height;
        internal uint RowStrideBytes;
        internal StormAovOrigin Origin;
        internal uint Flags;
        internal StormAovDepthConvention DepthConvention;
        internal uint Reserved;
        internal ulong DataOffset;
        internal ulong DataBytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Identity
    {
        internal uint StructSize;
        internal uint Version;
        internal uint Status;
        internal uint Reserved;
        internal int PrimId;
        internal int InstanceId;
        internal int InstanceIndex;
        internal uint PrimPathOffset;
        internal uint PrimPathLength;
        internal uint InstancerPathOffset;
        internal uint InstancerPathLength;
        internal uint ContextOffset;
        internal uint ContextCount;
        internal uint Reserved2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct InstanceContext
    {
        internal uint StructSize;
        internal uint Version;
        internal uint PathOffset;
        internal uint PathLength;
        internal int InstanceIndex;
        internal uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct View
    {
        internal uint StructSize;
        internal uint Version;
        internal uint OutputCount;
        internal uint IdentityCount;
        internal uint ContextCount;
        internal uint TextByteCount;
        internal uint Width;
        internal uint Height;
        internal StormAovStatus IdentityStatus;
        internal uint RevisionFlags;
        internal ulong CaptureId;
        internal ulong StateRevision;
        internal ulong SceneRevision;
        internal double TimeCode;
        internal ulong OwnedBytes;
        internal ulong AdmittedWorkingBytes;
        internal ulong RetainedScratchUpperBoundBytes;
        internal Output* Outputs;
        internal void* PixelData;
        internal ulong PixelBytes;
        internal Identity* Identities;
        internal InstanceContext* Contexts;
        internal byte* Text;
        internal uint* IdentityIndices;
        internal ulong IdentityIndexCount;
        internal NativeRenderCamera AppliedCamera;

        internal static View Create() => new()
        {
            StructSize = (uint)Unsafe.SizeOf<View>(),
            Version = StormAovNative.Version,
        };
    }
}
