// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// Backend-neutral pick command-list contracts driven through the public RHI on
/// real pixels.
/// </summary>
/// <remarks>
/// The subprim pick is two rendering scopes on one command list: a surface
/// pre-pass that writes depth and tokens, and an edge or point pass that reuses
/// that depth after the pick colour is cleared between them. The clear is the
/// only thing that stops the surface token from surviving into the answer, so a
/// backend that folds the two scopes into one render pass has to reproduce it
/// exactly -- including for a second scope that rasterizes nothing at all, which
/// is the case with no draw to read a clear rectangle from.
/// </remarks>
internal static class SilkPickCommandListConformance
{
    private const uint Size = 16;

    /// <summary>
    /// An empty second pick scope erases the surface pre-pass's token, so the
    /// pixel answers zero rather than the token underneath it.
    /// </summary>
    internal static async Task EmptySubprimPassAfterSurfacePrepassAnswersTokenZero(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat format)
    {
        ArgumentNullException.ThrowIfNull(device);
        var picking = (ISilkPickingGraphicsDevice)device;
        using ISilkPickGraphicsPipeline surface =
            picking.CreatePickGraphicsPipeline(
                SilkPickPipelineDescriptor.CreateChecked(
                    format,
                    SilkPickPrimitiveTopology.TriangleList));
        using ISilkPickGraphicsPipeline edges =
            picking.CreatePickGraphicsPipeline(
                SilkPickPipelineDescriptor.CreateChecked(
                    format,
                    SilkPickPrimitiveTopology.LineList,
                    SilkVertexLayoutDescriptor.PositionNormal,
                    SilkPickDepthBias.Coincident));
        using ISilkPickReadbackBuffer readback = picking.CreatePickReadbackBuffer();
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            new SilkTextureDescriptor(
                Size,
                Size,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using ISilkGraphicsBuffer vertices = CreateFullTriangle(device);
        using ISilkGraphicsBuffer indices = CreateTriangleIndices(device);
        using ISilkGraphicsBuffer uniforms = CreateIdentityUniform(device);
        var coordinate = new SilkTexturePixelCoordinate(8, 8);

        // The positive control. Without it a zero answer below would be
        // indistinguishable from a surface pass that never covered the pixel.
        byte[] withoutSubprimPass = Submit(
            device,
            readback,
            color,
            depth,
            record: commands =>
            {
                RecordSurfacePass(
                    commands,
                    surface,
                    color,
                    depth,
                    vertices,
                    indices,
                    uniforms,
                    coordinate);
            },
            coordinate);

        byte[] withEmptySubprimPass = Submit(
            device,
            readback,
            color,
            depth,
            record: commands =>
            {
                RecordSurfacePass(
                    commands,
                    surface,
                    color,
                    depth,
                    vertices,
                    indices,
                    uniforms,
                    coordinate);

                // The edge scope every eligible mesh declined: a bound pipeline,
                // a viewport, a scissor, and no draw at all.
                commands.ClearColor(color, new SilkColor(0, 0, 0, 0));
                commands.BeginRendering(new SilkRenderingDescriptor(color, depth));
                ((ISilkPickGraphicsCommandList)commands)
                    .SetPickGraphicsPipeline(edges);
                commands.SetViewport(new SilkViewport(0, 0, Size, Size));
                commands.SetScissor(new SilkScissor(
                    checked((int)coordinate.X),
                    checked((int)coordinate.Y),
                    1,
                    1));
                commands.EndRendering();
            },
            coordinate);

        await Assert.That(SilkPickTokenEncoding.Decode(withoutSubprimPass))
            .IsEqualTo(0x11223344U)
            .Because("The surface pre-pass must cover the picked pixel.");
        await Assert.That(SilkPickTokenEncoding.Decode(withEmptySubprimPass))
            .IsEqualTo(0U)
            .Because(
                "An empty edge pass must clear the surface pre-pass's token, " +
                $"but read {Convert.ToHexString(withEmptySubprimPass)}.");
    }

    private static void RecordSurfacePass(
        ISilkGraphicsCommandList commands,
        ISilkPickGraphicsPipeline pipeline,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        ISilkGraphicsBuffer vertices,
        ISilkGraphicsBuffer indices,
        ISilkGraphicsBuffer uniforms,
        SilkTexturePixelCoordinate coordinate)
    {
        var pickCommands = (ISilkPickGraphicsCommandList)commands;
        commands.BeginRendering(new SilkRenderingDescriptor(color, depth));
        pickCommands.SetPickGraphicsPipeline(pipeline);
        commands.SetViewport(new SilkViewport(0, 0, Size, Size));
        commands.SetScissor(new SilkScissor(
            checked((int)coordinate.X),
            checked((int)coordinate.Y),
            1,
            1));
        commands.SetVertexBuffer(vertices);
        commands.SetIndexBuffer(indices);
        commands.SetUniformBuffer(0, 0, uniforms);
        pickCommands.SetPickBaseToken(0x11223344);
        commands.DrawIndexed(3);
        commands.EndRendering();
    }

    private static byte[] Submit(
        ISilkGraphicsDevice device,
        ISilkPickReadbackBuffer readback,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        Action<ISilkGraphicsCommandList> record,
        SilkTexturePixelCoordinate coordinate)
    {
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.ClearColor(color, new SilkColor(0, 0, 0, 0));
        commands.ClearDepth(depth, 1);
        record(commands);
        ((ISilkPickGraphicsCommandList)commands).CopyRgba8Pixel(
            color,
            coordinate,
            readback);
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        submission.Wait();
        var bytes = new byte[SilkPickTokenEncoding.ByteSize];
        readback.ReadRgba8Pixel(bytes);
        return bytes;
    }

    private static ISilkGraphicsBuffer CreateFullTriangle(ISilkGraphicsDevice device)
    {
        ISilkGraphicsBuffer buffer = device.CreateBuffer(
            72,
            SilkBufferUsage.Vertex | SilkBufferUsage.Upload);
        buffer.Write(MemoryMarshal.AsBytes<float>(
        [
            -1, -1, 0.25f, 0, 0, 1,
            -1,  3, 0.25f, 0, 0, 1,
             3, -1, 0.25f, 0, 0, 1
        ]));
        return buffer;
    }

    private static ISilkGraphicsBuffer CreateTriangleIndices(
        ISilkGraphicsDevice device)
    {
        ISilkGraphicsBuffer buffer = device.CreateBuffer(
            12,
            SilkBufferUsage.Index | SilkBufferUsage.Upload);
        buffer.Write(MemoryMarshal.AsBytes<uint>([0, 1, 2]));
        return buffer;
    }

    private static ISilkGraphicsBuffer CreateIdentityUniform(
        ISilkGraphicsDevice device)
    {
        ISilkGraphicsBuffer buffer = device.CreateBuffer(
            80,
            SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
        buffer.Write(MemoryMarshal.AsBytes<float>(
        [
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1,
            1, 1, 1, 1
        ]));
        return buffer;
    }
}
