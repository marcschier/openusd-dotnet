// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// Runs the checked GPU deformation kernel on a real device and requires it to
/// reproduce the authoritative CPU result.
/// </summary>
/// <remarks>
/// <para>
/// This is the executable evidence for the GPU deformation slice. It builds a
/// rig, uploads the nine bounded buffers the kernel declares, dispatches it on
/// the device under test, reads the interleaved vertex buffer back, and
/// compares every position and normal against
/// <see cref="SilkDeformationEvaluator"/> -- the same oracle hdSilk's own
/// producer verifies its published rigs against. A backend that transposed a
/// matrix, reordered an accumulation, or mis-mapped a binding fails here with
/// the offending component named, rather than as a pixel difference somebody has
/// to attribute later.
/// </para>
/// <para>
/// The rigs are deliberately adversarial about the things a convention error
/// hides behind. The skinning matrices carry translation *and* non-uniform
/// scale, so a transpose changes the answer; the joints are asymmetric, so a
/// swapped palette index changes the answer; the blend shapes overlap on one
/// point across two ranges, so a regrouping that reordered the accumulation
/// changes the answer; and one joint scales anisotropically, so a normal
/// transformed by the matrix rather than by its inverse transpose changes the
/// answer.
/// </para>
/// </remarks>
public static class SilkDeformationComputeConformance
{
    private const float Tolerance = 2.0e-4f;

    /// <summary>
    /// Dispatches every rig case on the device and compares against the oracle.
    /// </summary>
    internal static async Task DeformationKernelMatchesTheCpuEvaluator(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!device.Capabilities.SupportsCompute)
        {
            Skip.Test("The device reports no compute capability.");
            return;
        }

        foreach ((string name, SilkMeshDeformationData rig) in BuildRigCases())
        {
            const uint strideFloats = 6;
            SilkDeformationGpuFallback fallback = SilkDeformationGpuPayload.TryBuild(
                rig,
                strideFloats,
                rig.BindPointCount,
                hasTangents: false,
                SilkTopologyKind.TriangleList,
                out SilkDeformationGpuPayload? payload);
            await Assert.That(fallback)
                .IsEqualTo(SilkDeformationGpuFallback.None)
                .Because($"rig '{name}' must be eligible for the GPU path");

            float[] gpu = Dispatch(device, shaderFormat, payload!, strideFloats);

            float[] points = new float[rig.BindPointCount * 3];
            float[] normals = new float[rig.BindPointCount * 3];
            SilkDeformationEvaluator.EvaluatePoints(rig, points);
            bool hasNormals = SilkDeformationEvaluator.TryEvaluateNormals(rig, normals);
            await Assert.That(hasNormals).IsTrue();

            for (int point = 0; point < rig.BindPointCount; point++)
            {
                for (int component = 0; component < 3; component++)
                {
                    float expected = points[(point * 3) + component];
                    float actual = gpu[(point * (int)strideFloats) + component];
                    await Assert.That(actual)
                        .IsEqualTo(expected)
                        .Within(Tolerance * Math.Max(1.0f, Math.Abs(expected)))
                        .Because(
                            $"rig '{name}' point {point} component {component} " +
                            "diverged from the CPU evaluator");
                }
                for (int component = 0; component < 3; component++)
                {
                    float expected = normals[(point * 3) + component];
                    float actual = gpu[(point * (int)strideFloats) + 3 + component];
                    await Assert.That(actual)
                        .IsEqualTo(expected)
                        .Within(Tolerance)
                        .Because(
                            $"rig '{name}' normal {point} component {component} " +
                            "diverged from the CPU evaluator");
                }
            }
        }
    }

    /// <summary>
    /// Requires the kernel to leave every float outside the deformed range of a
    /// vertex untouched.
    /// </summary>
    /// <remarks>
    /// The kernel writes into the retained interleaved vertex buffer in place,
    /// so a stride or offset error would silently overwrite the texture
    /// coordinates a material samples through. The buffer is pre-filled with a
    /// sentinel and the untouched floats are required to survive.
    /// </remarks>
    internal static async Task DeformationKernelWritesOnlyPositionsAndNormals(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!device.Capabilities.SupportsCompute)
        {
            Skip.Test("The device reports no compute capability.");
            return;
        }

        // Twelve floats rather than eight: the kernel owns the position, the
        // normal and the texture coordinates it was handed, so the floats that
        // have to survive are the ones past the coordinate pair. The stride is
        // a multiple of four so the checked fill kernel, which writes float4
        // elements, seeds exactly the vertices under test.
        const uint strideFloats = 12;
        SilkMeshDeformationData rig = BuildRigCases()[0].Rig;
        float[] texCoords = new float[rig.BindPointCount * 2];
        for (int index = 0; index < texCoords.Length; index++)
        {
            texCoords[index] = 0.125f * (index + 1);
        }
        SilkDeformationGpuFallback fallback = SilkDeformationGpuPayload.TryBuild(
            rig,
            strideFloats,
            rig.BindPointCount,
            hasTangents: false,
            SilkTopologyKind.TriangleList,
            out SilkDeformationGpuPayload? payload,
            texCoords);
        await Assert.That(fallback).IsEqualTo(SilkDeformationGpuFallback.None);

        float[] seeded = Dispatch(
            device,
            shaderFormat,
            payload!,
            strideFloats,
            fill: true);
        float[] unseeded = SilkCheckedFillSeed.Expected(
            checked((int)(payload!.PointCount * strideFloats)));
        for (int point = 0; point < rig.BindPointCount; point++)
        {
            int baseIndex = point * (int)strideFloats;
            // Floats eight through eleven are past everything the kernel owns,
            // so the bytes the seed wrote there must survive a stride or offset
            // error.
            for (int trailing = 8; trailing < (int)strideFloats; trailing++)
            {
                await Assert.That(seeded[baseIndex + trailing])
                    .IsEqualTo(unseeded[baseIndex + trailing]);
            }
            // The coordinates a material samples through are passed straight
            // from the buffer the host uploaded, in order.
            await Assert.That(seeded[baseIndex + 6])
                .IsEqualTo(texCoords[point * 2]);
            await Assert.That(seeded[baseIndex + 7])
                .IsEqualTo(texCoords[(point * 2) + 1]);
            // Non-vacuity: the deformed range must actually have been rewritten,
            // otherwise the check above would hold for a kernel that wrote
            // nothing at all.
            bool rewritten = false;
            for (int component = 0; component < 6; component++)
            {
                if (seeded[baseIndex + component] != unseeded[baseIndex + component])
                {
                    rewritten = true;
                }
            }
            await Assert.That(rewritten).IsTrue();
        }
    }

    /// <summary>
    /// Requires every backend to reject a parameter buffer sized for the legacy
    /// checked block when the deformation layout declares a larger one.
    /// </summary>
    /// <remarks>
    /// A uniform slot's declared size is what a Vulkan descriptor range is
    /// written from and what every backend validates a bound buffer against. A
    /// layout that left the deformation block's size unstated fell back to the
    /// checked <c>compute.fill</c> block, which is half of it, so Vulkan
    /// described a 32-byte block as a 16-byte range and every backend accepted a
    /// buffer holding only the first half of the parameters. Rejecting the short
    /// buffer here is what shows the layout is carrying its own size.
    /// </remarks>
    internal static async Task DeformationParametersAreBoundedByTheirOwnSize(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!device.Capabilities.SupportsCompute)
        {
            Skip.Test("The device reports no compute capability.");
            return;
        }
        SilkComputeBindingLayoutDescriptor descriptor =
            SilkCheckedShaderAssets.DeformCompute.Layout;
        await Assert.That(descriptor.UniformSlot.ElementStride)
            .IsEqualTo(SilkDeformComputeReflection.ParameterByteSize);

        using ISilkComputeBindingLayout layout =
            device.CreateComputeBindingLayout(descriptor);
        using ISilkGraphicsShaderModule module = device.CreateShaderModule(
            SilkCheckedShaderAssets.LoadDeformCompute(shaderFormat));
        using ISilkComputeShaderProgram program = device.CreateComputeShaderProgram(
            new SilkComputeShaderProgramDescriptor(module, layout));
        using ISilkComputePipeline pipeline = device.CreateComputePipeline(
            new SilkComputePipelineDescriptor(
                program,
                SilkCheckedShaderAssets.DeformCompute.ThreadGroupSizeX,
                SilkCheckedShaderAssets.DeformCompute.ThreadGroupSizeY,
                SilkCheckedShaderAssets.DeformCompute.ThreadGroupSizeZ));
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.SetComputePipeline(pipeline);

        // The legacy checked block size, which is what an unstated size fell
        // back to on every backend.
        uint legacy = SilkCheckedShaderAssets.Compute.GetUniformByteSize(device.Backend);
        await Assert.That(legacy)
            .IsLessThan(SilkDeformComputeReflection.ParameterByteSize);
        using ISilkGraphicsBuffer tooSmall = device.CreateBuffer(
            legacy,
            SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
        await Assert.That(
            () => commands.SetComputeUniformBuffer(
                0,
                SilkDeformComputeReflection.ParametersBinding,
                tooSmall))
            .Throws<ArgumentException>()
            .Because("a buffer holding half the parameter block must be refused");

        using ISilkGraphicsBuffer exact = device.CreateBuffer(
            SilkDeformComputeReflection.ParameterByteSize,
            SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
        await Assert.That(
            () => commands.SetComputeUniformBuffer(
                0,
                SilkDeformComputeReflection.ParametersBinding,
                exact))
            .ThrowsNothing();
    }

    /// <summary>
    /// Requires a second dispatch of the same payload to produce the same bytes.
    /// </summary>
    /// <remarks>
    /// A retained deformation resource is keyed by the rig's identity, so the
    /// renderer skips a dispatch when the identity is unchanged. That is only
    /// sound if dispatching the same payload twice is the same as dispatching it
    /// once, which a kernel that accumulated into its destination instead of
    /// writing it would not satisfy.
    /// </remarks>
    internal static async Task DeformationKernelIsIdempotentForOneIdentity(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!device.Capabilities.SupportsCompute)
        {
            Skip.Test("The device reports no compute capability.");
            return;
        }

        const uint strideFloats = 6;
        SilkMeshDeformationData rig = BuildRigCases()[1].Rig;
        _ = SilkDeformationGpuPayload.TryBuild(
            rig,
            strideFloats,
            rig.BindPointCount,
            hasTangents: false,
            SilkTopologyKind.TriangleList,
            out SilkDeformationGpuPayload? payload);

        float[] first = Dispatch(device, shaderFormat, payload!, strideFloats);
        float[] second = Dispatch(
            device,
            shaderFormat,
            payload!,
            strideFloats,
            repeat: 2);
        for (int index = 0; index < first.Length; index++)
        {
            await Assert.That(second[index]).IsEqualTo(first[index]);
        }
    }

    private static float[] Dispatch(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat,
        SilkDeformationGpuPayload payload,
        uint strideFloats,
        bool fill = false,
        int repeat = 1)
    {
        SilkDeformComputeReflection reflection = SilkCheckedShaderAssets.DeformCompute;
        using ISilkComputeBindingLayout layout =
            device.CreateComputeBindingLayout(BuildLayout(reflection, strideFloats));
        using ISilkGraphicsShaderModule module = device.CreateShaderModule(
            SilkCheckedShaderAssets.LoadDeformCompute(shaderFormat));
        using ISilkComputeShaderProgram program = device.CreateComputeShaderProgram(
            new SilkComputeShaderProgramDescriptor(module, layout));
        using ISilkComputePipeline pipeline = device.CreateComputePipeline(
            new SilkComputePipelineDescriptor(
                program,
                reflection.ThreadGroupSizeX,
                reflection.ThreadGroupSizeY,
                reflection.ThreadGroupSizeZ));

        int vertexFloats = checked((int)(payload.PointCount * strideFloats));
        // The written buffer lives on the device heap rather than an upload
        // heap: Direct3D 12 refuses an unordered-access view over an upload
        // allocation, so a writable compute destination cannot be one. Every
        // read-only input stays uploadable, which is what lets the host write
        // the rig without a staging copy.
        using ISilkGraphicsBuffer vertices = device.CreateBuffer(
            checked((nuint)(vertexFloats * sizeof(float))),
            SilkBufferUsage.Storage);
        using ISilkGraphicsBuffer bindPose = CreateFloatBuffer(device, payload.BindPose);
        using ISilkGraphicsBuffer jointIndices =
            CreateUIntBuffer(device, payload.JointIndices);
        using ISilkGraphicsBuffer jointWeights =
            CreateFloatBuffer(device, payload.JointWeights);
        using ISilkGraphicsBuffer matrices = CreateFloatBuffer(device, payload.Matrices);
        using ISilkGraphicsBuffer blendWeights =
            CreateFloatBuffer(device, payload.BlendWeights);
        using ISilkGraphicsBuffer blendSpans = CreateUIntBuffer(device, payload.BlendSpans);
        using ISilkGraphicsBuffer blendDeltas =
            CreateFloatBuffer(device, payload.BlendDeltas);
        using ISilkGraphicsBuffer texCoords = CreateFloatBuffer(device, payload.TexCoords);
        using ISilkGraphicsBuffer parameters = device.CreateBuffer(
            checked((nuint)payload.Parameters.Length),
            SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
        parameters.Write(payload.Parameters);

        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        using SilkCheckedFillSeed? seed = fill
            ? new SilkCheckedFillSeed(device, shaderFormat, vertexFloats)
            : null;
        if (seed is not null)
        {
            // Seeding through the checked fill kernel does two jobs at once: it
            // puts a known pattern under the vertices so an out-of-range write
            // is visible, and it proves two kernels with different generalized
            // layouts share one command list and one destination buffer, which
            // is the whole point of generalizing the binding interface.
            seed.Record(commands, vertices);
            commands.BufferBarrier(vertices);
        }
        for (int pass = 0; pass < repeat; pass++)
        {
            commands.SetComputePipeline(pipeline);
            commands.SetStorageBuffer(0, SilkDeformComputeReflection.VerticesBinding, vertices);
            commands.SetStorageBuffer(0, SilkDeformComputeReflection.BindPoseBinding, bindPose);
            commands.SetStorageBuffer(
                0,
                SilkDeformComputeReflection.JointIndicesBinding,
                jointIndices);
            commands.SetStorageBuffer(
                0,
                SilkDeformComputeReflection.JointWeightsBinding,
                jointWeights);
            commands.SetStorageBuffer(
                0,
                SilkDeformComputeReflection.MatricesBinding,
                matrices);
            commands.SetStorageBuffer(
                0,
                SilkDeformComputeReflection.BlendWeightsBinding,
                blendWeights);
            commands.SetStorageBuffer(
                0,
                SilkDeformComputeReflection.BlendSpansBinding,
                blendSpans);
            commands.SetStorageBuffer(
                0,
                SilkDeformComputeReflection.BlendDeltasBinding,
                blendDeltas);
            commands.SetStorageBuffer(
                0,
                SilkDeformComputeReflection.TexCoordsBinding,
                texCoords);
            commands.SetComputeUniformBuffer(
                0,
                SilkDeformComputeReflection.ParametersBinding,
                parameters);
            commands.Dispatch(payload.PointCount);
            commands.BufferBarrier(vertices);
        }
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        submission.Wait();

        byte[] raw = new byte[vertexFloats * sizeof(float)];
        vertices.ReadbackForTesting(raw);
        float[] result = new float[vertexFloats];
        for (int index = 0; index < vertexFloats; index++)
        {
            result[index] = BinaryPrimitives.ReadSingleLittleEndian(
                raw.AsSpan(index * sizeof(float), sizeof(float)));
        }
        return result;
    }

    /// <summary>
    /// The kernel's own declared layout, with the writable slot's stride set to
    /// the vertex stride this dispatch writes.
    /// </summary>
    /// <remarks>
    /// The stride is what bounds the dispatch against the vertex buffer before
    /// a single command is recorded, so it is the runtime stride rather than the
    /// reflected element size of a float.
    /// </remarks>
    private static SilkComputeBindingLayoutDescriptor BuildLayout(
        SilkDeformComputeReflection reflection,
        uint strideFloats)
    {
        List<SilkComputeSlot> slots = [.. reflection.Layout.Slots];
        slots[0] = slots[0] with { ElementStride = strideFloats * sizeof(float) };
        return new SilkComputeBindingLayoutDescriptor(slots);
    }

    private static ISilkGraphicsBuffer CreateFloatBuffer(
        ISilkGraphicsDevice device,
        float[] values)
    {
        float[] source = values.Length == 0 ? [0] : values;
        byte[] bytes = new byte[source.Length * sizeof(float)];
        for (int index = 0; index < source.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(index * sizeof(float), sizeof(float)),
                source[index]);
        }
        ISilkGraphicsBuffer buffer = device.CreateBuffer(
            checked((nuint)bytes.Length),
            SilkBufferUsage.Storage | SilkBufferUsage.Upload);
        buffer.Write(bytes);
        return buffer;
    }

    private static ISilkGraphicsBuffer CreateUIntBuffer(
        ISilkGraphicsDevice device,
        uint[] values)
    {
        uint[] source = values.Length == 0 ? [0] : values;
        byte[] bytes = new byte[source.Length * sizeof(uint)];
        for (int index = 0; index < source.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(index * sizeof(uint), sizeof(uint)),
                source[index]);
        }
        ISilkGraphicsBuffer buffer = device.CreateBuffer(
            checked((nuint)bytes.Length),
            SilkBufferUsage.Storage | SilkBufferUsage.Upload);
        buffer.Write(bytes);
        return buffer;
    }

    private static List<(string Name, SilkMeshDeformationData Rig)> BuildRigCases()
    {
        List<(string, SilkMeshDeformationData)> cases = [];

        // A rigid single joint that both translates and scales, so a transposed
        // matrix moves the answer.
        float[] scaleTranslate = Identity();
        scaleTranslate[0] = 2.0f;
        scaleTranslate[5] = 3.0f;
        scaleTranslate[10] = 0.5f;
        scaleTranslate[12] = 7.0f;
        scaleTranslate[13] = -4.0f;
        scaleTranslate[14] = 11.0f;
        cases.Add((
            "single-joint-scale-translate",
            SilkDeformationRigBuilder.Build(
                bindPoints: [1, 2, 3, -4, 5, -6, 0.25f, 0.5f, 0.75f],
                bindNormals: [0, 0, 1, 0.6f, 0.8f, 0, 1, 0, 0],
                influencesPerPoint: 1,
                jointIndices: [0, 0, 0],
                jointWeights: [1, 1, 1],
                jointMatrices: scaleTranslate,
                geomBindTransform: Identity())));

        // Two asymmetric joints with a split influence, plus a geom bind
        // transform that is not the identity, so a dropped bind transform or a
        // swapped palette index moves the answer.
        float[] rotateX = Identity();
        rotateX[5] = 0;
        rotateX[6] = 1;
        rotateX[9] = -1;
        rotateX[10] = 0;
        float[] shear = Identity();
        shear[1] = 0.35f;
        shear[8] = -0.2f;
        shear[13] = 2.5f;
        float[] geomBind = Identity();
        geomBind[0] = 1.5f;
        geomBind[12] = 0.5f;
        geomBind[14] = -0.25f;
        cases.Add((
            "two-joint-split-influence",
            SilkDeformationRigBuilder.Build(
                bindPoints: [1, 0, 0, 0, 1, 0, 0.3f, -0.7f, 1.1f, 2, 2, 2],
                bindNormals: [0, 0, 1, 0, 1, 0, 0.70710678f, 0.70710678f, 0, 1, 0, 0],
                influencesPerPoint: 2,
                jointIndices: [0, 1, 1, 0, 0, 1, 1, 0],
                jointWeights: [1, 0, 0.25f, 0.75f, 0.5f, 0.5f, 0, 1],
                jointMatrices: [.. rotateX, .. shear],
                geomBindTransform: geomBind)));

        // Two blend ranges that overlap on one point, which is what a resolved
        // in-between and its primary shape do, plus a joint that still moves the
        // result, so a regrouping that reordered the accumulation moves it too.
        cases.Add((
            "overlapping-blend-ranges",
            SilkDeformationRigBuilder.Build(
                bindPoints: [1, 1, 1, 2, 2, 2, 3, 3, 3],
                bindNormals: [0, 0, 1, 0, 1, 0, 1, 0, 0],
                influencesPerPoint: 1,
                jointIndices: [0, 0, 0],
                jointWeights: [1, 1, 1],
                jointMatrices: shear,
                geomBindTransform: Identity(),
                blendRanges: [(0, 2, 0.5f), (2, 1, 0.25f)],
                blendDeltaPoints: [0, 2, 0],
                blendDeltaPositions: [4, 0, 0, 0, -3, 0, 8, 1, -1],
                blendDeltaNormals: [0.1f, 0.2f, 0.3f, 0, 0, 0.5f, -0.4f, 0, 0])));

        return cases;
    }

    private static float[] Identity() =>
    [
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1
    ];
}
