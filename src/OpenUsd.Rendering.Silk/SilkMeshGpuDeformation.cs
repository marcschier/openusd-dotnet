// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// The retained GPU state of one deformed geometry: the buffers the checked
/// deformation kernel reads, and the identity of the pose last dispatched into
/// the vertex buffer it writes.
/// </summary>
/// <remarks>
/// <para>
/// The split between what is uploaded once and what is re-uploaded is the whole
/// point of the resource. The bind pose, the influence streams and the emitted
/// texture coordinates never move while a rig is retained, so they are written
/// when the resource is created and never again. The joint palette, the
/// resolved sub-shape weights and the gathered deltas are the pose, so they are
/// re-uploaded exactly when the rig's identity changes -- which is what makes a
/// repeated frame free and a scrubbed time code cost one dispatch.
/// </para>
/// <para>
/// The resource is also the unit of teardown. Every buffer it owns is disposed
/// with it, and the geometry that owns it is already reference counted and
/// retired by the retained scene, so a prim that stops being deformed, a page
/// that retires it, and a disposed renderer all release the same way.
/// </para>
/// </remarks>
internal sealed class SilkMeshGpuDeformation : IDisposable
{
    private readonly ISilkGraphicsBuffer _bindPose;
    private readonly ISilkGraphicsBuffer _jointIndices;
    private readonly ISilkGraphicsBuffer _jointWeights;
    private readonly ISilkGraphicsBuffer _texCoords;
    private ISilkGraphicsBuffer _matrices;
    private ISilkGraphicsBuffer _blendWeights;
    private ISilkGraphicsBuffer _blendSpans;
    private ISilkGraphicsBuffer _blendDeltas;
    private ISilkGraphicsBuffer _parameters;
    private bool _disposed;

    internal SilkMeshGpuDeformation(
        ISilkComputePipeline pipeline,
        SilkDeformationGpuPayload payload,
        ISilkGraphicsBuffer bindPose,
        ISilkGraphicsBuffer jointIndices,
        ISilkGraphicsBuffer jointWeights,
        ISilkGraphicsBuffer texCoords,
        ISilkGraphicsBuffer matrices,
        ISilkGraphicsBuffer blendWeights,
        ISilkGraphicsBuffer blendSpans,
        ISilkGraphicsBuffer blendDeltas,
        ISilkGraphicsBuffer parameters)
    {
        Pipeline = pipeline;
        PointCount = payload.PointCount;
        PendingIdentity = payload.Identity;
        _bindPose = bindPose;
        _jointIndices = jointIndices;
        _jointWeights = jointWeights;
        _texCoords = texCoords;
        _matrices = matrices;
        _blendWeights = blendWeights;
        _blendSpans = blendSpans;
        _blendDeltas = blendDeltas;
        _parameters = parameters;
    }

    /// <summary>Gets the pipeline built for this geometry's vertex stride.</summary>
    internal ISilkComputePipeline Pipeline { get; }

    /// <summary>Gets the number of points one dispatch covers.</summary>
    internal uint PointCount { get; }

    /// <summary>Gets the pose identity whose inputs are currently uploaded.</summary>
    internal ulong PendingIdentity { get; private set; }

    /// <summary>Gets the pose identity currently written into the vertex buffer.</summary>
    internal ulong DispatchedIdentity { get; private set; }

    /// <summary>Gets whether a dispatch is required before the next draw.</summary>
    internal bool NeedsDispatch => DispatchedIdentity != PendingIdentity;

    /// <summary>
    /// Re-uploads the pose-dependent inputs, growing a buffer only when the new
    /// pose needs more room than the last one.
    /// </summary>
    /// <remarks>
    /// The sizes change between poses because the resolved sub-shape set does:
    /// an in-between that becomes active adds a range and its deltas. Growing
    /// rather than reallocating keeps a scrub through a blend-shape animation
    /// from churning allocations once it has seen its widest pose.
    /// </remarks>
    internal void UpdatePose(
        ISilkGraphicsDevice device,
        SilkDeformationGpuPayload payload)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Identity == PendingIdentity)
        {
            return;
        }
        SilkDeformationGpuBuffers.WriteGrowing(device, ref _matrices, payload.Matrices);
        SilkDeformationGpuBuffers.WriteGrowing(
            device,
            ref _blendWeights,
            payload.BlendWeights);
        SilkDeformationGpuBuffers.WriteGrowing(device, ref _blendSpans, payload.BlendSpans);
        SilkDeformationGpuBuffers.WriteGrowing(
            device,
            ref _blendDeltas,
            payload.BlendDeltas);
        SilkDeformationGpuBuffers.WriteGrowing(
            device,
            ref _parameters,
            payload.Parameters);
        PendingIdentity = payload.Identity;
    }

    /// <summary>
    /// Records one dispatch and the barrier that makes its writes visible to
    /// the vertex fetch of every later draw.
    /// </summary>
    internal void Record(ISilkGraphicsCommandList commands, ISilkGraphicsBuffer vertices)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(vertices);
        commands.SetComputePipeline(Pipeline);
        commands.SetStorageBuffer(0, SilkDeformComputeReflection.VerticesBinding, vertices);
        commands.SetStorageBuffer(0, SilkDeformComputeReflection.BindPoseBinding, _bindPose);
        commands.SetStorageBuffer(
            0,
            SilkDeformComputeReflection.JointIndicesBinding,
            _jointIndices);
        commands.SetStorageBuffer(
            0,
            SilkDeformComputeReflection.JointWeightsBinding,
            _jointWeights);
        commands.SetStorageBuffer(
            0,
            SilkDeformComputeReflection.MatricesBinding,
            _matrices);
        commands.SetStorageBuffer(
            0,
            SilkDeformComputeReflection.BlendWeightsBinding,
            _blendWeights);
        commands.SetStorageBuffer(
            0,
            SilkDeformComputeReflection.BlendSpansBinding,
            _blendSpans);
        commands.SetStorageBuffer(
            0,
            SilkDeformComputeReflection.BlendDeltasBinding,
            _blendDeltas);
        commands.SetStorageBuffer(
            0,
            SilkDeformComputeReflection.TexCoordsBinding,
            _texCoords);
        commands.SetComputeUniformBuffer(
            0,
            SilkDeformComputeReflection.ParametersBinding,
            _parameters);
        commands.Dispatch(PointCount);
        commands.BufferBarrier(vertices);
    }

    /// <summary>Marks the recorded dispatch as the one now in the buffer.</summary>
    internal void MarkDispatched() => DispatchedIdentity = PendingIdentity;

    /// <summary>
    /// Forgets what the vertex buffer holds without touching the uploads.
    /// </summary>
    /// <remarks>
    /// A device generation change invalidates what the device is holding, not
    /// what the host uploaded, so the pose stays valid and only the claim that
    /// it already reached the vertex buffer is dropped. The next frame
    /// dispatches once and the buffer is correct again.
    /// </remarks>
    internal void InvalidateDispatch() => DispatchedIdentity = 0;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _parameters.Dispose();
        _blendDeltas.Dispose();
        _blendSpans.Dispose();
        _blendWeights.Dispose();
        _matrices.Dispose();
        _texCoords.Dispose();
        _jointWeights.Dispose();
        _jointIndices.Dispose();
        _bindPose.Dispose();
        Pipeline.Dispose();
    }
}

/// <summary>Uploads the kernel's inputs as whole arrays.</summary>
/// <remarks>
/// Every buffer here is written once per pose from one contiguous span. Nothing
/// is written per point, per joint or per blend shape, and no element crosses a
/// managed-to-native boundary on its own.
/// </remarks>
internal static class SilkDeformationGpuBuffers
{
    /// <summary>Creates an uploadable storage buffer holding one float array.</summary>
    internal static ISilkGraphicsBuffer Create(
        ISilkGraphicsDevice device,
        ReadOnlySpan<float> values)
    {
        ISilkGraphicsBuffer buffer = device.CreateBuffer(
            ByteSize(values.Length * sizeof(float)),
            SilkBufferUsage.Storage | SilkBufferUsage.Upload);
        Write(buffer, values);
        return buffer;
    }

    /// <summary>Creates an uploadable storage buffer holding one uint array.</summary>
    internal static ISilkGraphicsBuffer Create(
        ISilkGraphicsDevice device,
        ReadOnlySpan<uint> values)
    {
        ISilkGraphicsBuffer buffer = device.CreateBuffer(
            ByteSize(values.Length * sizeof(uint)),
            SilkBufferUsage.Storage | SilkBufferUsage.Upload);
        Write(buffer, values);
        return buffer;
    }

    /// <summary>Creates the kernel's parameter buffer.</summary>
    internal static ISilkGraphicsBuffer CreateParameters(
        ISilkGraphicsDevice device,
        ReadOnlySpan<byte> values)
    {
        ISilkGraphicsBuffer buffer = device.CreateBuffer(
            ByteSize(values.Length),
            SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
        buffer.Write(values);
        return buffer;
    }

    internal static void WriteGrowing(
        ISilkGraphicsDevice device,
        ref ISilkGraphicsBuffer buffer,
        ReadOnlySpan<float> values)
    {
        nuint required = ByteSize(values.Length * sizeof(float));
        if (buffer.Size < required)
        {
            buffer.Dispose();
            buffer = device.CreateBuffer(
                required,
                SilkBufferUsage.Storage | SilkBufferUsage.Upload);
        }
        Write(buffer, values);
    }

    internal static void WriteGrowing(
        ISilkGraphicsDevice device,
        ref ISilkGraphicsBuffer buffer,
        ReadOnlySpan<uint> values)
    {
        nuint required = ByteSize(values.Length * sizeof(uint));
        if (buffer.Size < required)
        {
            buffer.Dispose();
            buffer = device.CreateBuffer(
                required,
                SilkBufferUsage.Storage | SilkBufferUsage.Upload);
        }
        Write(buffer, values);
    }

    internal static void WriteGrowing(
        ISilkGraphicsDevice device,
        ref ISilkGraphicsBuffer buffer,
        ReadOnlySpan<byte> values)
    {
        nuint required = ByteSize(values.Length);
        if (buffer.Size < required)
        {
            buffer.Dispose();
            buffer = device.CreateBuffer(
                required,
                SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
        }
        buffer.Write(values);
    }

    private static void Write(ISilkGraphicsBuffer buffer, ReadOnlySpan<float> values)
    {
        if (values.IsEmpty)
        {
            return;
        }
        buffer.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(values));
    }

    private static void Write(ISilkGraphicsBuffer buffer, ReadOnlySpan<uint> values)
    {
        if (values.IsEmpty)
        {
            return;
        }
        buffer.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(values));
    }

    /// <summary>
    /// The allocation size of one array, never zero.
    /// </summary>
    /// <remarks>
    /// A rig with no blend shapes still binds a blend weight and delta buffer,
    /// because a kernel's binding table is fixed and every backend refuses a
    /// zero-sized allocation. The kernel never reads them: its own declared
    /// counts are zero.
    /// </remarks>
    private static nuint ByteSize(int length) =>
        checked((nuint)Math.Max(length, sizeof(uint)));
}
