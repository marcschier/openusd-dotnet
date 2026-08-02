// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

internal static class OffscreenRhiConformance
{
    internal static async Task ClearReadbackAndDisposal(ISilkGraphicsDevice device)
    {
        ISilkGraphicsTexture texture = device.CreateTexture2D(
            4,
            3,
            SilkTextureFormat.Rgba8Unorm);
        ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.ClearColor(texture, new SilkColor(0, 0, 1, 1));
        commands.ClearColor(texture, new SilkColor(1, 0, 0, 1));
        ISilkGraphicsSubmission submission = device.Submit(commands);

        commands.Dispose();
        submission.Wait();
        await Assert.That(submission.IsCompleted).IsTrue();

        byte[] actual = new byte[4 * 3 * 4];
        texture.ReadbackForTesting(actual);
        byte[] expected = new byte[actual.Length];
        for (int offset = 0; offset < expected.Length; offset += 4)
        {
            expected[offset] = byte.MaxValue;
            expected[offset + 3] = byte.MaxValue;
        }
        bool bytesMatch = actual.AsSpan().SequenceEqual(expected);
        await Assert.That(bytesMatch).IsTrue();

        submission.Dispose();
        texture.Dispose();
        await Assert.That(
            () => submission.Wait()).Throws<ObjectDisposedException>();
        await Assert.That(
            () => commands.ClearColor(texture, new SilkColor(0, 0, 0, 1)))
            .Throws<ObjectDisposedException>();
        await Assert.That(
            () => texture.ReadbackForTesting(actual)).Throws<ObjectDisposedException>();

        submission.Dispose();
        commands.Dispose();
        texture.Dispose();
    }

    internal static async Task SubmittedTextureSurvivesEarlyDispose(
        ISilkGraphicsDevice device)
    {
        ISilkGraphicsTexture texture = device.CreateTexture2D(64, 64);
        ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.ClearColor(texture, new SilkColor(0, 1, 0, 1));
        ISilkGraphicsSubmission submission = device.Submit(commands);

        commands.Dispose();
        texture.Dispose();

        byte[] destination = new byte[64 * 64 * 4];
        await Assert.That(
            () => texture.ReadbackForTesting(destination))
            .Throws<ObjectDisposedException>();
        using (ISilkGraphicsCommandList rejectedCommands = device.CreateCommandList())
        {
            await Assert.That(
                () => rejectedCommands.ClearColor(
                    texture,
                    new SilkColor(1, 0, 0, 1)))
                .Throws<ObjectDisposedException>();
        }
        InvalidOperationException disposeException =
            Assert.Throws<InvalidOperationException>(device.Dispose);
        await Assert.That(disposeException.Message)
            .Contains("buffers, textures, or submissions");

        submission.Wait();

        await Assert.That(submission.IsCompleted).IsTrue();
        Assert.Throws<InvalidOperationException>(device.Dispose);
        submission.Dispose();

        using ISilkGraphicsTexture replacement = device.CreateTexture2D(1, 1);
    }

    internal static async Task SubmitFailureReleasesAcquiredLeases(
        ISilkGraphicsDevice device)
    {
        ISilkGraphicsTexture firstTexture = device.CreateTexture2D(1, 1);
        ISilkGraphicsTexture disposedTexture = device.CreateTexture2D(1, 1);
        ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.ClearColor(firstTexture, new SilkColor(1, 0, 0, 1));
        commands.ClearColor(disposedTexture, new SilkColor(0, 1, 0, 1));
        disposedTexture.Dispose();

        await Assert.That(() => device.Submit(commands))
            .Throws<ObjectDisposedException>();

        commands.Dispose();
        firstTexture.Dispose();
        device.Dispose();
    }

    internal static async Task ReadbackWaitsForPendingSubmission(
        ISilkGraphicsDevice device)
    {
        using ISilkGraphicsTexture texture = device.CreateTexture2D(8, 8);
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.ClearColor(texture, new SilkColor(0, 0, 1, 1));
        using ISilkGraphicsSubmission submission = device.Submit(commands);

        byte[] actual = new byte[8 * 8 * 4];
        texture.ReadbackForTesting(actual);

        byte[] expected = new byte[actual.Length];
        for (int offset = 0; offset < expected.Length; offset += 4)
        {
            expected[offset + 2] = byte.MaxValue;
            expected[offset + 3] = byte.MaxValue;
        }
        await Assert.That(actual.AsSpan().SequenceEqual(expected)).IsTrue();
        await Assert.That(submission.IsCompleted).IsTrue();
    }

    internal static async Task DepthClearReadbackAndLifetime(
        ISilkGraphicsDevice device)
    {
        ISilkGraphicsTexture texture = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(4, 3));
        ISilkGraphicsCommandList commands = device.CreateCommandList();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => commands.ClearDepth(texture, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => commands.ClearDepth(texture, -0.01f));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => commands.ClearDepth(texture, 1.01f));
        Assert.Throws<InvalidOperationException>(
            () => commands.ClearColor(texture, new SilkColor(0, 0, 0, 1)));
        commands.ClearDepth(texture, 0.25f);
        commands.ClearDepth(texture, 0.75f);
        ISilkGraphicsSubmission submission = device.Submit(commands);
        commands.Dispose();

        float[] actual = new float[4 * 3];
        texture.ReadbackForTesting(actual);

        await Assert.That(actual.All(value => value == 0.75f)).IsTrue();
        await Assert.That(texture.Format).IsEqualTo(SilkTextureFormat.D32Float);
        await Assert.That(texture.Usage).IsEqualTo(SilkTextureUsage.DepthRenderTarget);
        submission.Wait();
        submission.Dispose();
        texture.Dispose();

        ISilkGraphicsTexture disposedTexture = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(64, 64));
        ISilkGraphicsCommandList disposedCommands = device.CreateCommandList();
        disposedCommands.ClearDepth(disposedTexture, 0.5f);
        ISilkGraphicsSubmission disposedSubmission = device.Submit(disposedCommands);
        disposedCommands.Dispose();
        disposedTexture.Dispose();

        await Assert.That(
            () => disposedTexture.ReadbackForTesting(new float[64 * 64]))
            .Throws<ObjectDisposedException>();
        using (ISilkGraphicsCommandList rejectedCommands = device.CreateCommandList())
        {
            await Assert.That(
                () => rejectedCommands.ClearDepth(disposedTexture, 0.5f))
                .Throws<ObjectDisposedException>();
        }

        disposedSubmission.Wait();
        disposedSubmission.Dispose();
    }

    internal static async Task CrossDeviceDepthTargetIsRejected(
        ISilkGraphicsDevice textureDevice,
        ISilkGraphicsDevice commandDevice)
    {
        using ISilkGraphicsTexture texture = textureDevice.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(1, 1));
        using ISilkGraphicsCommandList commands = commandDevice.CreateCommandList();

        await Assert.That(() => commands.ClearDepth(texture, 0.5f))
            .Throws<ArgumentException>();
    }

    internal static async Task TextureUploadReadbackAndLifetime(
        ISilkGraphicsDevice device)
    {
        byte[] first = CreatePattern(4, 3, 17);
        byte[] second = CreatePattern(4, 3, 91);
        using ISilkGraphicsTexture texture = device.CreateTexture2D(
            SilkTextureDescriptor.SampledRgba8(4, 3));

        using (ISilkGraphicsCommandList invalidCommands = device.CreateCommandList())
        {
            await Assert.That(
                () => invalidCommands.UploadTexture(
                    texture,
                    first.AsSpan(0, first.Length - 1)))
                .Throws<ArgumentException>();
            await Assert.That(() => invalidCommands.UploadTexture(texture, new byte[49]))
                .Throws<ArgumentException>();
        }

        using (ISilkGraphicsCommandList commands = device.CreateCommandList())
        {
            commands.UploadTexture(texture, first);
            using ISilkGraphicsSubmission submission = device.Submit(commands);
            byte[] actual = new byte[first.Length];
            texture.ReadbackForTesting(actual);
            await Assert.That(actual.AsSpan().SequenceEqual(first)).IsTrue();
            await Assert.That(submission.IsCompleted).IsTrue();
        }

        using (ISilkGraphicsCommandList commands = device.CreateCommandList())
        {
            commands.UploadTexture(texture, second);
            using ISilkGraphicsSubmission submission = device.Submit(commands);
            submission.Wait();
        }
        byte[] overwritten = new byte[second.Length];
        texture.ReadbackForTesting(overwritten);
        await Assert.That(overwritten.AsSpan().SequenceEqual(second)).IsTrue();
        await Assert.That(texture.Usage.HasFlag(SilkTextureUsage.Sampled)).IsTrue();
        await Assert.That(texture.Usage.HasFlag(SilkTextureUsage.CopyDestination)).IsTrue();

        using ISilkGraphicsTexture nonUploadTexture = device.CreateTexture2D(1, 1);
        using (ISilkGraphicsCommandList commands = device.CreateCommandList())
        {
            await Assert.That(
                () => commands.UploadTexture(nonUploadTexture, new byte[4]))
                .Throws<InvalidOperationException>();
        }

        ISilkGraphicsTexture disposedTexture = device.CreateTexture2D(
            SilkTextureDescriptor.SampledRgba8(64, 64));
        ISilkGraphicsCommandList disposedCommands = device.CreateCommandList();
        disposedCommands.UploadTexture(
            disposedTexture,
            CreatePattern(64, 64, 31));
        ISilkGraphicsSubmission disposedSubmission = device.Submit(disposedCommands);
        disposedCommands.Dispose();
        disposedTexture.Dispose();

        await Assert.That(
            () => disposedTexture.ReadbackForTesting(new byte[64 * 64 * 4]))
            .Throws<ObjectDisposedException>();
        using (ISilkGraphicsCommandList rejectedCommands = device.CreateCommandList())
        {
            await Assert.That(
                () => rejectedCommands.UploadTexture(
                    disposedTexture,
                    new byte[64 * 64 * 4]))
                .Throws<ObjectDisposedException>();
        }
        disposedSubmission.Wait();
        disposedSubmission.Dispose();
    }

    internal static async Task CrossDeviceUploadIsRejected(
        ISilkGraphicsDevice textureDevice,
        ISilkGraphicsDevice commandDevice)
    {
        using ISilkGraphicsTexture texture = textureDevice.CreateTexture2D(
            SilkTextureDescriptor.SampledRgba8(1, 1));
        using ISilkGraphicsCommandList commands = commandDevice.CreateCommandList();

        await Assert.That(() => commands.UploadTexture(texture, new byte[4]))
            .Throws<ArgumentException>();
    }

    internal static async Task SamplerCreationAndDisposal(ISilkGraphicsDevice device)
    {
        ISilkGraphicsSampler sampler = device.CreateSampler(
            SilkSamplerDescriptor.LinearClamp);

        await Assert.That(sampler.Descriptor.MinFilter)
            .IsEqualTo(SilkSamplerFilter.Linear);
        await Assert.That(sampler.Descriptor.AddressU)
            .IsEqualTo(SilkSamplerAddressMode.ClampToEdge);
        InvalidOperationException disposeException =
            Assert.Throws<InvalidOperationException>(device.Dispose);
        await Assert.That(disposeException.Message).Contains("samplers");

        sampler.Dispose();
        sampler.Dispose();
        using ISilkGraphicsSampler replacement = device.CreateSampler(
            SilkSamplerDescriptor.NearestRepeat);
        await Assert.That(replacement.Descriptor.MagFilter)
            .IsEqualTo(SilkSamplerFilter.Nearest);
        await Assert.That(replacement.Descriptor.AddressW)
            .IsEqualTo(SilkSamplerAddressMode.Repeat);
    }

    internal static async Task DrawsIndexedTriangle(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat)
    {
        const uint size = 64;
        byte[] pixels = RenderCheckedTriangle(
            device, shaderFormat, SilkBindingLayoutDescriptor.SceneParameters);
        byte[] background = Pixel(pixels, size, 2, 2).ToArray();
        byte[] interior = Pixel(pixels, size, 32, 32).ToArray();
        await Assert.That(background.SequenceEqual(new byte[] { 0, 0, 0, 255 })).IsTrue();
        // The triangle faces the deterministic headlight and carries a white tint,
        // so a lit interior is white. It was red only because the placeholder shader
        // returned abs(normal) and these vertices carried a normal of (1, 0, 0).
        await Assert.That(interior[0]).IsGreaterThanOrEqualTo((byte)240);
        await Assert.That(interior[1]).IsGreaterThanOrEqualTo((byte)240);
        await Assert.That(interior[2]).IsGreaterThanOrEqualTo((byte)240);
        await Assert.That(interior[3]).IsGreaterThanOrEqualTo((byte)240);
    }

    internal static async Task MaterialBindingLayoutDrawsIdenticallyToSceneParameters(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat)
    {
        SilkBindingLayoutDescriptor material = SilkBindingLayoutDescriptor.ForMaterial(
        [
            new SilkBindingSlot(
                0, 1, SilkBindingKind.UniformBuffer, 64, SilkShaderStageVisibility.Fragment),
            new SilkBindingSlot(
                0, 2, SilkBindingKind.SampledTexture, 0, SilkShaderStageVisibility.Fragment),
            new SilkBindingSlot(
                0, 3, SilkBindingKind.SampledTexture, 0, SilkShaderStageVisibility.Fragment),
            new SilkBindingSlot(
                0, 4, SilkBindingKind.Sampler, 0, SilkShaderStageVisibility.Fragment),
        ]);

        byte[] baseline = RenderCheckedTriangle(
            device, shaderFormat, SilkBindingLayoutDescriptor.SceneParameters);
        byte[] withMaterial = RenderCheckedTriangle(device, shaderFormat, material);

        // The material slots widen the root signature / descriptor set layout without
        // changing what the shader binds, so the image must be byte-identical. A
        // difference would mean the wider layout shifted the SceneParameters binding.
        await Assert.That(withMaterial.SequenceEqual(baseline)).IsTrue();
        await Assert.That(baseline.Any(static value => value != 0)).IsTrue();
    }

    internal static async Task MaterialResourcesBindToADrawWithoutPerturbingIt(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat)
    {
        SilkBindingLayoutDescriptor material = SilkBindingLayoutDescriptor.ForMaterial(
        [
            new SilkBindingSlot(
                0, 1, SilkBindingKind.SampledTexture, 0, SilkShaderStageVisibility.Fragment),
            new SilkBindingSlot(
                0, 2, SilkBindingKind.Sampler, 0, SilkShaderStageVisibility.Fragment),
        ]);

        byte[] baseline = RenderCheckedTriangle(
            device, shaderFormat, SilkBindingLayoutDescriptor.SceneParameters);
        byte[] withResources = RenderCheckedTriangle(
            device,
            shaderFormat,
            material,
            bindMaterialResources: true);

        // The checked mesh shader does not sample yet, so the proof available today is
        // that binding a real texture and sampler is accepted end to end by the backend
        // and leaves the draw untouched. Sampling correctness arrives with the
        // UsdPreviewSurface shader permutations.
        await Assert.That(withResources.SequenceEqual(baseline)).IsTrue();
        await Assert.That(baseline.Any(static value => value != 0)).IsTrue();
    }

    internal static async Task MaterialBindingRejectsResourcesTheLayoutDoesNotDeclare(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat)
    {
        SilkBindingLayoutDescriptor material = SilkBindingLayoutDescriptor.ForMaterial(
        [
            new SilkBindingSlot(
                0, 1, SilkBindingKind.SampledTexture, 0, SilkShaderStageVisibility.Fragment),
        ]);
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(64, 64));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(64, 64));
        using ISilkGraphicsTexture sampled = device.CreateTexture2D(
            new SilkTextureDescriptor(
                4,
                4,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination));
        using ISilkGraphicsSampler sampler = device.CreateSampler(
            SilkSamplerDescriptor.LinearClamp);
        using ISilkGraphicsShaderModule vertexShader = device.CreateShaderModule(
            SilkCheckedShaderAssets.LoadMeshVertex(shaderFormat));
        using ISilkGraphicsShaderModule fragmentShader = device.CreateShaderModule(
            SilkCheckedShaderAssets.LoadMeshFragment(shaderFormat));
        using ISilkGraphicsBindingLayout bindingLayout =
            device.CreateBindingLayout(material);
        using ISilkGraphicsShaderProgram program = device.CreateShaderProgram(
            new SilkShaderProgramDescriptor(vertexShader, fragmentShader, bindingLayout));
        using ISilkGraphicsPipeline pipeline = device.CreateGraphicsPipeline(
            new SilkGraphicsPipelineDescriptor(
                program,
                SilkVertexLayoutDescriptor.PositionNormal,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureFormat.D32Float));
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.BeginRendering(new SilkRenderingDescriptor(color, depth));
        commands.SetGraphicsPipeline(pipeline);

        // Slot 1 is a texture, so binding a sampler there must be rejected rather than
        // written into a table the shader would read as the wrong kind.
        await Assert.That(() => commands.SetSampler(0, 1, sampler))
            .Throws<InvalidOperationException>();

        // Slot 2 is not declared at all.
        await Assert.That(() => commands.SetTexture(0, 2, sampled))
            .Throws<InvalidOperationException>();

        // A render target is not a sampled texture.
        await Assert.That(() => commands.SetTexture(0, 1, color))
            .Throws<ArgumentException>();

        commands.EndRendering();
    }

    private static byte[] RenderCheckedTriangle(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat,
        SilkBindingLayoutDescriptor layout,
        bool bindMaterialResources = false)
    {
        const uint size = 64;
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(size, size));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(size, size));
        using ISilkGraphicsShaderModule vertexShader = device.CreateShaderModule(
            SilkCheckedShaderAssets.LoadMeshVertex(shaderFormat));
        using ISilkGraphicsShaderModule fragmentShader = device.CreateShaderModule(
            SilkCheckedShaderAssets.LoadMeshFragment(shaderFormat));
        using ISilkGraphicsBindingLayout bindingLayout = device.CreateBindingLayout(layout);
        using ISilkGraphicsShaderProgram program = device.CreateShaderProgram(
            new SilkShaderProgramDescriptor(
                vertexShader,
                fragmentShader,
                bindingLayout));
        using ISilkGraphicsPipeline pipeline = device.CreateGraphicsPipeline(
            new SilkGraphicsPipelineDescriptor(
                program,
                SilkVertexLayoutDescriptor.PositionNormal,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureFormat.D32Float));
        using ISilkGraphicsBuffer vertices = device.CreateBuffer(
            72,
            SilkBufferUsage.Vertex | SilkBufferUsage.Upload);
        using ISilkGraphicsBuffer indices = device.CreateBuffer(
            12,
            SilkBufferUsage.Index | SilkBufferUsage.Upload);
        using ISilkGraphicsBuffer uniforms = device.CreateBuffer(
            80,
            SilkBufferUsage.Uniform | SilkBufferUsage.Storage | SilkBufferUsage.Upload);
        using ISilkGraphicsBuffer surfaceConstants = CreateSurfaceConstants(device);
        using ISilkGraphicsBuffer frameConstants = CreateFrameConstants(device);
        vertices.Write(MemoryMarshal.AsBytes<float>(
        [
            -0.75f, -0.75f, 0, 0, 0, 1,
             0.00f,  0.75f, 0, 0, 0, 1,
             0.75f, -0.75f, 0, 0, 0, 1
        ]));
        indices.Write(MemoryMarshal.AsBytes<uint>([0, 2, 1]));
        uniforms.Write(MemoryMarshal.AsBytes<float>(
        [
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1,
            1, 1, 1, 1
        ]));
        ISilkGraphicsTexture? materialTexture = null;
        ISilkGraphicsSampler? materialSampler = null;
        try
        {
            if (bindMaterialResources)
            {
                materialTexture = device.CreateTexture2D(
                    new SilkTextureDescriptor(
                        4,
                        4,
                        SilkTextureFormat.Rgba8Unorm,
                        SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination));
                materialSampler = device.CreateSampler(SilkSamplerDescriptor.LinearClamp);
            }

            using ISilkGraphicsCommandList commands = device.CreateCommandList();
            commands.ClearColor(color, new SilkColor(0, 0, 0, 1));
            commands.ClearDepth(depth, 1);
            if (materialTexture is not null)
            {
                commands.UploadTexture(materialTexture, new byte[4 * 4 * 4]);
            }
            commands.BeginRendering(new SilkRenderingDescriptor(color, depth));
            commands.SetGraphicsPipeline(pipeline);
            if (materialTexture is not null && materialSampler is not null)
            {
                commands.SetTexture(0, 1, materialTexture);
                commands.SetSampler(0, 2, materialSampler);
            }
            commands.SetViewport(new SilkViewport(0, 0, size, size));
            commands.SetScissor(new SilkScissor(0, 0, size, size));
            commands.SetVertexBuffer(vertices);
            commands.SetIndexBuffer(indices);
            commands.SetUniformBuffer(0, 0, uniforms);
            BindAlwaysOnSlots(commands, uniforms, surfaceConstants, frameConstants);
            commands.DrawIndexed(3);
            commands.EndRendering();
            using ISilkGraphicsSubmission submission = device.Submit(commands);
            submission.Wait();

            byte[] pixels = new byte[size * size * 4];
            color.ReadbackForTesting(pixels);
            return pixels;
        }
        finally
        {
            materialSampler?.Dispose();
            materialTexture?.Dispose();
        }
    }

    internal static async Task IndexedDrawSubmissionLeasesResources(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat)
    {
        ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(64, 64));
        ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(64, 64));
        ISilkGraphicsShaderModule vertexShader = device.CreateShaderModule(
            SilkCheckedShaderAssets.LoadMeshVertex(shaderFormat));
        ISilkGraphicsShaderModule fragmentShader = device.CreateShaderModule(
            SilkCheckedShaderAssets.LoadMeshFragment(shaderFormat));
        ISilkGraphicsBindingLayout bindingLayout = device.CreateBindingLayout(
            SilkBindingLayoutDescriptor.SceneParameters);
        ISilkGraphicsShaderProgram program = device.CreateShaderProgram(
            new SilkShaderProgramDescriptor(
                vertexShader,
                fragmentShader,
                bindingLayout));
        ISilkGraphicsPipeline pipeline = device.CreateGraphicsPipeline(
            new SilkGraphicsPipelineDescriptor(
                program,
                SilkVertexLayoutDescriptor.PositionNormal,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureFormat.D32Float));
        ISilkGraphicsBuffer vertices = device.CreateBuffer(
            72,
            SilkBufferUsage.Vertex | SilkBufferUsage.Upload);
        ISilkGraphicsBuffer indices = device.CreateBuffer(
            12,
            SilkBufferUsage.Index | SilkBufferUsage.Upload);
        ISilkGraphicsBuffer uniforms = device.CreateBuffer(
            80,
            SilkBufferUsage.Uniform | SilkBufferUsage.Storage | SilkBufferUsage.Upload);
        ISilkGraphicsBuffer surfaceConstants = CreateSurfaceConstants(device);
        ISilkGraphicsBuffer frameConstants = CreateFrameConstants(device);
        vertices.Write(MemoryMarshal.AsBytes<float>(
        [
            -0.75f, -0.75f, 0, 0, 0, 1,
             0.00f,  0.75f, 0, 0, 0, 1,
             0.75f, -0.75f, 0, 0, 0, 1
        ]));
        indices.Write(MemoryMarshal.AsBytes<uint>([0, 2, 1]));
        uniforms.Write(MemoryMarshal.AsBytes<float>(
        [
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1,
            1, 1, 1, 1
        ]));
        ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.ClearColor(color, new SilkColor(0, 0, 0, 1));
        commands.ClearDepth(depth, 1);
        commands.BeginRendering(new SilkRenderingDescriptor(color, depth));
        commands.SetGraphicsPipeline(pipeline);
        commands.SetViewport(new SilkViewport(0, 0, 64, 64));
        commands.SetScissor(new SilkScissor(0, 0, 64, 64));
        commands.SetVertexBuffer(vertices);
        commands.SetIndexBuffer(indices);
        commands.SetUniformBuffer(0, 0, uniforms);
        BindAlwaysOnSlots(commands, uniforms, surfaceConstants, frameConstants);
        commands.DrawIndexed(3);
        commands.EndRendering();
        ISilkGraphicsSubmission submission = device.Submit(commands);

        commands.Dispose();
        color.Dispose();
        depth.Dispose();
        pipeline.Dispose();
        program.Dispose();
        vertexShader.Dispose();
        fragmentShader.Dispose();
        bindingLayout.Dispose();
        vertices.Dispose();
        indices.Dispose();
        surfaceConstants.Dispose();
        frameConstants.Dispose();
        uniforms.Dispose();

        await Assert.That(
            () => color.ReadbackForTesting(new byte[64 * 64 * 4]))
            .Throws<ObjectDisposedException>();
        await Assert.That(() => vertices.Write([1])).Throws<ObjectDisposedException>();
        submission.Wait();
        await Assert.That(submission.IsCompleted).IsTrue();
        submission.Dispose();
    }

    internal static async Task RejectsCrossDeviceGraphicsResources(
        ISilkGraphicsDevice resourceDevice,
        ISilkGraphicsDevice commandDevice,
        SilkShaderBinaryFormat shaderFormat)
    {
        using ISilkGraphicsShaderModule vertexShader =
            resourceDevice.CreateShaderModule(
                SilkCheckedShaderAssets.LoadMeshVertex(shaderFormat));
        using ISilkGraphicsShaderModule fragmentShader =
            resourceDevice.CreateShaderModule(
                SilkCheckedShaderAssets.LoadMeshFragment(shaderFormat));
        using ISilkGraphicsBindingLayout layout =
            resourceDevice.CreateBindingLayout(
                SilkBindingLayoutDescriptor.SceneParameters);

        await Assert.That(
            () => commandDevice.CreateShaderProgram(
                new SilkShaderProgramDescriptor(vertexShader, fragmentShader, layout)))
            .Throws<ArgumentException>();

        using ISilkGraphicsShaderProgram program =
            resourceDevice.CreateShaderProgram(
                new SilkShaderProgramDescriptor(vertexShader, fragmentShader, layout));
        SilkVertexLayoutDescriptor wrongLayout =
            SilkVertexLayoutDescriptor.PositionNormal with { Stride = 12 };
        await Assert.That(
            () => resourceDevice.CreateGraphicsPipeline(
                new SilkGraphicsPipelineDescriptor(
                    program,
                    wrongLayout,
                    SilkTextureFormat.Rgba8Unorm,
                    SilkTextureFormat.D32Float)))
            .Throws<ArgumentException>();
        ISilkGraphicsPipeline disposedPipeline =
            resourceDevice.CreateGraphicsPipeline(
                new SilkGraphicsPipelineDescriptor(
                    program,
                    SilkVertexLayoutDescriptor.PositionNormal,
                    SilkTextureFormat.Rgba8Unorm,
                    SilkTextureFormat.D32Float));
        disposedPipeline.Dispose();

        using ISilkGraphicsTexture color = resourceDevice.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(1, 1));
        using ISilkGraphicsTexture depth = resourceDevice.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(1, 1));
        using ISilkGraphicsCommandList commands = commandDevice.CreateCommandList();
        await Assert.That(
            () => commands.BeginRendering(new SilkRenderingDescriptor(color, depth)))
            .Throws<ArgumentException>();
        using ISilkGraphicsCommandList disposedCommands =
            resourceDevice.CreateCommandList();
        disposedCommands.BeginRendering(new SilkRenderingDescriptor(color, depth));
        await Assert.That(
            () => disposedCommands.SetGraphicsPipeline(disposedPipeline))
            .Throws<ObjectDisposedException>();
        disposedCommands.EndRendering();
    }

    internal static async Task PreservesOrderedGraphicsCommands(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat)
    {
        const uint size = 64;
        using var resources = new OrderedTriangleResources(
            device,
            shaderFormat,
            size);

        using (ISilkGraphicsCommandList commands = device.CreateCommandList())
        {
            commands.ClearColor(resources.Color, new SilkColor(0, 0, 0, 1));
            commands.ClearDepth(resources.Depth, 1);
            RecordOrderedDraw(
                commands,
                resources,
                new SilkViewport(16, 8, 32, 40),
                new SilkScissor(16, 8, 32, 40));
            using ISilkGraphicsSubmission submission = device.Submit(commands);
            submission.Wait();
        }

        byte[] pixels = new byte[size * size * 4];
        resources.Color.ReadbackForTesting(pixels);
        (int MinX, int MinY, int MaxX, int MaxY, int Count) litBounds =
            FindLitBounds(pixels, size);
        Console.WriteLine(
            $"{device.Backend} transformed lit bounds: " +
            $"{litBounds.MinX},{litBounds.MinY}-{litBounds.MaxX},{litBounds.MaxY}; " +
            $"pixels={litBounds.Count}");
        (int X, int Y) transformedInterior = FindLitPixel(
            pixels,
            size,
            static (x, y) => x >= 34 && x < 44 && y >= 16 && y < 40);
        (int X, int Y) scissorExcluded = FindLitPixel(
            pixels,
            size,
            static (x, y) => x < 34 && y >= 16 && y < 40);
        byte[] viewportExcluded = Pixel(pixels, size, 50, 30).ToArray();
        await Assert.That(litBounds.Count).IsGreaterThan(0);
        await Assert.That(viewportExcluded.SequenceEqual(
            new byte[] { 0, 0, 0, 255 })).IsTrue();

        using (ISilkGraphicsCommandList commands = device.CreateCommandList())
        {
            commands.ClearColor(resources.Color, new SilkColor(0, 0, 0, 1));
            commands.ClearDepth(resources.Depth, 1);
            RecordOrderedDraw(
                commands,
                resources,
                new SilkViewport(16, 8, 32, 40),
                new SilkScissor(34, 16, 10, 24));
            using ISilkGraphicsSubmission submission = device.Submit(commands);
            submission.Wait();
        }
        resources.Color.ReadbackForTesting(pixels);
        byte[] retainedPixel = Pixel(
            pixels,
            size,
            checked((uint)transformedInterior.X),
            checked((uint)transformedInterior.Y)).ToArray();
        byte[] clippedPixel = Pixel(
            pixels,
            size,
            checked((uint)scissorExcluded.X),
            checked((uint)scissorExcluded.Y)).ToArray();
        await Assert.That(retainedPixel[0]).IsGreaterThanOrEqualTo((byte)240);
        await Assert.That(retainedPixel[1]).IsGreaterThanOrEqualTo((byte)240);
        await Assert.That(retainedPixel[2]).IsGreaterThanOrEqualTo((byte)240);
        await Assert.That(clippedPixel.SequenceEqual(
            new byte[] { 0, 0, 0, 255 })).IsTrue();

        using (ISilkGraphicsCommandList commands = device.CreateCommandList())
        {
            commands.ClearDepth(resources.Depth, 1);
            RecordOrderedDraw(
                commands,
                resources,
                new SilkViewport(0, 0, size, size),
                new SilkScissor(0, 0, size, size));
            commands.ClearColor(resources.Color, new SilkColor(0, 1, 0, 1));
            using ISilkGraphicsSubmission submission = device.Submit(commands);
            submission.Wait();
        }
        resources.Color.ReadbackForTesting(pixels);
        await Assert.That(AllPixelsEqual(pixels, 0, 255, 0, 255)).IsTrue();

        using (ISilkGraphicsCommandList commands = device.CreateCommandList())
        {
            commands.ClearColor(resources.Color, new SilkColor(0, 0, 1, 1));
            commands.ClearDepth(resources.Depth, 1);
            RecordOrderedDraw(
                commands,
                resources,
                new SilkViewport(0, 0, size, size),
                new SilkScissor(0, 0, size, size));
            commands.ClearColor(resources.Color, new SilkColor(1, 1, 0, 1));
            using ISilkGraphicsSubmission submission = device.Submit(commands);
            submission.Wait();
        }
        resources.Color.ReadbackForTesting(pixels);
        await Assert.That(AllPixelsEqual(pixels, 255, 255, 0, 255)).IsTrue();
    }

    internal static async Task DispatchesCheckedComputeKernels(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat)
    {
        const uint elementCount = 67;
        using var resources = new ComputeResources(device, shaderFormat, elementCount);
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        resources.RecordFillAndScale(commands, elementCount);
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        submission.Wait();

        byte[] bytes = new byte[checked((int)elementCount * 16)];
        resources.Output.ReadbackForTesting(bytes);
        AssertComputeValues(bytes, elementCount, 3);
        await Assert.That(submission.IsCompleted).IsTrue();
    }

    internal static async Task ComputeSubmissionLeasesResources(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat)
    {
        const uint elementCount = 67;
        var resources = new ComputeResources(device, shaderFormat, elementCount);
        ISilkGraphicsCommandList commands = device.CreateCommandList();
        resources.RecordFillAndScale(commands, elementCount);
        ISilkGraphicsSubmission submission = device.Submit(commands);

        commands.Dispose();
        resources.Dispose();

        await Assert.That(
            () => resources.Output.ReadbackForTesting(
                new byte[checked((int)elementCount * 16)]))
            .Throws<ObjectDisposedException>();
        await Assert.That(
            () => resources.FillUniform.Write([1]))
            .Throws<ObjectDisposedException>();
        submission.Wait();
        await Assert.That(submission.IsCompleted).IsTrue();
        submission.Dispose();
    }

    internal static async Task RejectsInvalidComputeResources(
        ISilkGraphicsDevice resourceDevice,
        ISilkGraphicsDevice commandDevice,
        SilkShaderBinaryFormat shaderFormat)
    {
        const uint elementCount = 67;
        using var resources = new ComputeResources(
            resourceDevice,
            shaderFormat,
            elementCount);
        using ISilkGraphicsCommandList commands = commandDevice.CreateCommandList();

        await Assert.That(() => commands.SetComputePipeline(resources.FillPipeline))
            .Throws<ArgumentException>();
        await Assert.That(() => commands.SetStorageBuffer(0, 0, resources.Output))
            .Throws<ArgumentException>();

        using ISilkGraphicsCommandList localCommands = resourceDevice.CreateCommandList();
        await Assert.That(
            () => localCommands.SetStorageBuffer(0, 1, resources.Output))
            .Throws<ArgumentException>();
        await Assert.That(
            () => localCommands.SetComputeUniformBuffer(0, 0, resources.FillUniform))
            .Throws<ArgumentException>();
        localCommands.SetComputePipeline(resources.FillPipeline);
        localCommands.SetStorageBuffer(0, 0, resources.Output);
        localCommands.SetComputeUniformBuffer(0, 1, resources.FillUniform);
        await Assert.That(() => localCommands.Dispatch(0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => localCommands.Dispatch(elementCount + 1))
            .Throws<ArgumentOutOfRangeException>();

        resources.FillPipeline.Dispose();
        using ISilkGraphicsCommandList disposedCommands =
            resourceDevice.CreateCommandList();
        await Assert.That(
            () => disposedCommands.SetComputePipeline(resources.FillPipeline))
            .Throws<ObjectDisposedException>();
    }

    internal static async Task InterleavesGraphicsAndComputeCommands(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat)
    {
        const uint elementCount = 67;
        const uint size = 64;
        using var compute = new ComputeResources(device, shaderFormat, elementCount);
        using var graphics = new OrderedTriangleResources(device, shaderFormat, size);
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        compute.RecordFill(commands, elementCount);
        commands.BufferBarrier(compute.Output);
        commands.ClearColor(graphics.Color, new SilkColor(0, 0, 0, 1));
        commands.ClearDepth(graphics.Depth, 1);
        RecordOrderedDraw(
            commands,
            graphics,
            new SilkViewport(0, 0, size, size),
            new SilkScissor(0, 0, size, size));
        compute.RecordScale(commands, elementCount);
        commands.BufferBarrier(compute.Output);
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        submission.Wait();

        byte[] values = new byte[checked((int)elementCount * 16)];
        compute.Output.ReadbackForTesting(values);
        AssertComputeValues(values, elementCount, 3);
        byte[] pixels = new byte[size * size * 4];
        graphics.Color.ReadbackForTesting(pixels);
        await Assert.That(Pixel(pixels, size, 32, 32)[0])
            .IsGreaterThanOrEqualTo((byte)240);
    }

    internal static async Task ComputeOutputFeedsVertexBuffer(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat)
    {
        const uint size = 64;
        const uint elementCount = 5;
        using var compute = new ComputeResources(
            device,
            shaderFormat,
            elementCount,
            SilkBufferUsage.Storage | SilkBufferUsage.Vertex,
            0.25f);
        using var graphics = new OrderedTriangleResources(device, shaderFormat, size);
        using ISilkGraphicsBuffer indices = device.CreateBuffer(
            12,
            SilkBufferUsage.Index | SilkBufferUsage.Upload);
        indices.Write(MemoryMarshal.AsBytes<uint>([0, 2, 1]));
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        compute.RecordFill(commands, elementCount);
        commands.BufferBarrier(compute.Output);
        commands.ClearColor(graphics.Color, new SilkColor(0, 0, 0, 1));
        commands.ClearDepth(graphics.Depth, 1);
        RecordDraw(commands, graphics, compute.Output, indices);
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        submission.Wait();

        byte[] values = new byte[checked((int)elementCount * 16)];
        compute.Output.ReadbackForTesting(values);
        AssertComputeValues(values, elementCount, 0.25f);
        byte[] pixels = new byte[checked((int)(size * size * 4))];
        graphics.Color.ReadbackForTesting(pixels);
        int coloredPixels = CountNonBlackPixels(pixels);
        Console.WriteLine(
            $"{device.Backend} compute vertex draw colored pixels: {coloredPixels}");
        await Assert.That(coloredPixels).IsGreaterThan(0);
    }

    internal static async Task ComputeOutputFeedsIndexBuffer(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat)
    {
        const uint size = 64;
        const uint elementCount = 1;
        using var compute = new ComputeResources(
            device,
            shaderFormat,
            elementCount,
            SilkBufferUsage.Storage | SilkBufferUsage.Index,
            1);
        using var graphics = new OrderedTriangleResources(device, shaderFormat, size);
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        compute.RecordFill(commands, elementCount);
        commands.BufferBarrier(compute.Output);
        commands.ClearColor(graphics.Color, new SilkColor(0, 0, 0, 1));
        commands.ClearDepth(graphics.Depth, 1);
        RecordDraw(commands, graphics, graphics.Vertices, compute.Output);
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        submission.Wait();

        byte[] values = new byte[16];
        compute.Output.ReadbackForTesting(values);
        AssertComputeValues(values, elementCount, 1);
        byte[] pixels = new byte[checked((int)(size * size * 4))];
        graphics.Color.ReadbackForTesting(pixels);
        await Assert.That(AllPixelsEqual(pixels, 0, 0, 0, 255)).IsTrue();
    }

    internal static async Task DispatchBoundariesAndOverflow(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat)
    {
        foreach (uint elementCount in new uint[] { 1, 63, 64, 65 })
        {
            using var resources = new ComputeResources(
                device,
                shaderFormat,
                elementCount);
            using ISilkGraphicsCommandList commands = device.CreateCommandList();
            resources.RecordFill(commands, elementCount);
            commands.BufferBarrier(resources.Output);
            using ISilkGraphicsSubmission submission = device.Submit(commands);
            submission.Wait();
            byte[] values = new byte[checked((int)elementCount * 16)];
            resources.Output.ReadbackForTesting(values);
            AssertComputeValues(values, elementCount, 1.5f);
        }

        using var overflowResources = new ComputeResources(device, shaderFormat, 65);
        using ISilkGraphicsCommandList overflowCommands = device.CreateCommandList();
        overflowCommands.SetComputePipeline(overflowResources.FillPipeline);
        overflowCommands.SetStorageBuffer(0, 0, overflowResources.Output);
        overflowCommands.SetComputeUniformBuffer(0, 1, overflowResources.FillUniform);
        await Assert.That(() => overflowCommands.Dispatch(uint.MaxValue))
            .Throws<ArgumentOutOfRangeException>();
    }

    private static void RecordDraw(
        ISilkGraphicsCommandList commands,
        OrderedTriangleResources resources,
        ISilkGraphicsBuffer vertices,
        ISilkGraphicsBuffer indices)
    {
        commands.BeginRendering(new SilkRenderingDescriptor(
            resources.Color,
            resources.Depth));
        commands.SetGraphicsPipeline(resources.Pipeline);
        commands.SetViewport(new SilkViewport(0, 0, resources.Color.Width, resources.Color.Height));
        commands.SetScissor(new SilkScissor(0, 0, resources.Color.Width, resources.Color.Height));
        commands.SetVertexBuffer(vertices);
        commands.SetIndexBuffer(indices);
        commands.SetUniformBuffer(0, 0, resources.Uniforms);
        BindAlwaysOnSlots(
            commands,
            resources.Uniforms,
            resources.SurfaceConstants,
            resources.FrameConstants);
        commands.DrawIndexed(3);
        commands.EndRendering();
    }

    /// <summary>
    /// Binds the slots the checked mesh shaders read on every draw: the instance
    /// table the vertex stage indexes, and the surface constants the fragment stage
    /// reads. Leaving either unbound renders correctly on D3D12 and Vulkan, whose
    /// reflection-driven binding aliases the slot onto the uniform buffer, and
    /// renders nothing at all on Metal.
    /// </summary>
    private static void BindAlwaysOnSlots(
        ISilkGraphicsCommandList commands,
        ISilkGraphicsBuffer uniforms,
        ISilkGraphicsBuffer surfaceConstants,
        ISilkGraphicsBuffer frameConstants)
    {
        commands.SetStorageBuffer(0, 6, uniforms);
        commands.SetStorageBuffer(
            0,
            SilkBindingLayoutDescriptor.SurfaceParametersBinding,
            surfaceConstants);
        commands.SetStorageBuffer(
            0,
            SilkBindingLayoutDescriptor.FrameParametersBinding,
            frameConstants);
    }

    /// <summary>
    /// Creates the default surface constants: no material, so the shaded flag is
    /// zero and the scene tint drives diffuse, lit by the deterministic headlight.
    /// </summary>
    private static ISilkGraphicsBuffer CreateSurfaceConstants(ISilkGraphicsDevice device)
    {
        ISilkGraphicsBuffer buffer = device.CreateBuffer(
            128,
            SilkBufferUsage.Storage | SilkBufferUsage.Upload);
        buffer.Write(MemoryMarshal.AsBytes<float>(
        [
            0.18f, 0.18f, 0.18f, 1,
            0, 0, 0, 1,
            0, 0, 0, 1.5f,
            0, 0.5f, 0, 0,
            0, 0.01f, 0, 0,
            0, 0, 1, 1,
            1, 1, 1, 1,
            0, 0, 0, 0
        ]));
        return buffer;
    }

    /// <summary>
    /// Byte size of the shader's frame constants block, mirroring
    /// <c>FrameParameters</c> in <c>eng/shaders/sources/mesh.slang</c>.
    /// </summary>
    /// <remarks>
    /// This was 208 bytes until page ABI 9 added per-frame lighting, which moved
    /// <c>eyeToWorld</c> to offset 480. A 208-byte buffer therefore read out of
    /// bounds. D3D12 and Vulkan on Windows returned values that happened to
    /// render correctly; SwiftShader on Linux returned zeros, so the triangle
    /// came back unlit and only the Linux leg of CI failed.
    /// </remarks>
    private const int FrameConstantsByteSize = 544;

    private static ISilkGraphicsBuffer CreateFrameConstants(ISilkGraphicsDevice device)
    {
        ISilkGraphicsBuffer buffer = device.CreateBuffer(
            FrameConstantsByteSize,
            SilkBufferUsage.Storage | SilkBufferUsage.Upload);
        var values = new byte[FrameConstantsByteSize];
        Span<float> floats = MemoryMarshal.Cast<byte, float>(values.AsSpan());
        // clipToEye at 0 and eyeToWorld at 480 must both be identity. The light
        // block stays zero on purpose: the shader treats that as "no scene
        // lighting" and falls back to the deterministic headlight carried in the
        // surface constants, which is what these RHI cases are asserting.
        floats[0] = 1;
        floats[5] = 1;
        floats[10] = 1;
        floats[15] = 1;
        floats[120] = 1;
        floats[125] = 1;
        floats[130] = 1;
        floats[135] = 1;
        buffer.Write(values);
        return buffer;
    }

    private static int CountNonBlackPixels(ReadOnlySpan<byte> pixels)
    {
        int count = 0;
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset] != 0 || pixels[offset + 1] != 0 || pixels[offset + 2] != 0)
            {
                count++;
            }
        }
        return count;
    }

    private static void AssertComputeValues(
        ReadOnlySpan<byte> bytes,
        uint elementCount,
        float multiplier)
    {
        ReadOnlySpan<float> values = MemoryMarshal.Cast<byte, float>(bytes);
        for (uint index = 0; index < elementCount; index++)
        {
            int offset = checked((int)index * 4);
            if (values[offset] != index * multiplier ||
                values[offset + 1] != 0 ||
                values[offset + 2] != 0 ||
                values[offset + 3] != 1)
            {
                throw new InvalidOperationException(
                    $"Unexpected compute value at {index}: " +
                    $"{values[offset]}, {values[offset + 1]}, " +
                    $"{values[offset + 2]}, {values[offset + 3]}.");
            }
        }
    }

    private static void RecordOrderedDraw(
        ISilkGraphicsCommandList commands,
        OrderedTriangleResources resources,
        SilkViewport viewport,
        SilkScissor scissor)
    {
        commands.BeginRendering(new SilkRenderingDescriptor(
            resources.Color,
            resources.Depth));
        commands.SetGraphicsPipeline(resources.Pipeline);
        commands.SetViewport(viewport);
        commands.SetScissor(scissor);
        commands.SetVertexBuffer(resources.Vertices);
        commands.SetIndexBuffer(resources.Indices);
        commands.SetUniformBuffer(0, 0, resources.Uniforms);
        BindAlwaysOnSlots(
            commands,
            resources.Uniforms,
            resources.SurfaceConstants,
            resources.FrameConstants);
        commands.DrawIndexed(3);
        commands.EndRendering();
    }

    private static bool AllPixelsEqual(
        ReadOnlySpan<byte> pixels,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset] != red ||
                pixels[offset + 1] != green ||
                pixels[offset + 2] != blue ||
                pixels[offset + 3] != alpha)
            {
                return false;
            }
        }
        return true;
    }

    private static (int MinX, int MinY, int MaxX, int MaxY, int Count) FindLitBounds(
        ReadOnlySpan<byte> pixels,
        uint width)
    {
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = -1;
        int maxY = -1;
        int count = 0;
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset] < 240 ||
                pixels[offset + 1] < 240 ||
                pixels[offset + 2] < 240)
            {
                continue;
            }
            int pixel = offset / 4;
            int x = pixel % checked((int)width);
            int y = pixel / checked((int)width);
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
            count++;
        }
        return (minX, minY, maxX, maxY, count);
    }

    private static (int X, int Y) FindLitPixel(
        ReadOnlySpan<byte> pixels,
        uint width,
        Func<int, int, bool> predicate)
    {
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset] < 240 ||
                pixels[offset + 1] < 240 ||
                pixels[offset + 2] < 240)
            {
                continue;
            }
            int pixel = offset / 4;
            int x = pixel % checked((int)width);
            int y = pixel / checked((int)width);
            if (predicate(x, y))
            {
                return (x, y);
            }
        }
        throw new InvalidOperationException(
            "The transformed triangle did not cover a required evidence region.");
    }

    private static ReadOnlySpan<byte> Pixel(
        byte[] pixels,
        uint width,
        uint x,
        uint y) =>
        pixels.AsSpan(checked((int)((y * width + x) * 4)), 4);

    private sealed class OrderedTriangleResources : IDisposable
    {
        internal OrderedTriangleResources(
            ISilkGraphicsDevice device,
            SilkShaderBinaryFormat shaderFormat,
            uint size)
        {
            Color = device.CreateTexture2D(
                SilkTextureDescriptor.ColorTarget(size, size));
            Depth = device.CreateTexture2D(
                SilkTextureDescriptor.DepthTarget(size, size));
            VertexShader = device.CreateShaderModule(
                SilkCheckedShaderAssets.LoadMeshVertex(shaderFormat));
            FragmentShader = device.CreateShaderModule(
                SilkCheckedShaderAssets.LoadMeshFragment(shaderFormat));
            BindingLayout = device.CreateBindingLayout(
                SilkBindingLayoutDescriptor.SceneParameters);
            Program = device.CreateShaderProgram(new SilkShaderProgramDescriptor(
                VertexShader,
                FragmentShader,
                BindingLayout));
            Pipeline = device.CreateGraphicsPipeline(
                new SilkGraphicsPipelineDescriptor(
                    Program,
                    SilkVertexLayoutDescriptor.PositionNormal,
                    SilkTextureFormat.Rgba8Unorm,
                    SilkTextureFormat.D32Float));
            Vertices = device.CreateBuffer(
                144,
                SilkBufferUsage.Vertex | SilkBufferUsage.Upload);
            Indices = device.CreateBuffer(
                12,
                SilkBufferUsage.Index | SilkBufferUsage.Upload);
            Uniforms = device.CreateBuffer(
                80,
                SilkBufferUsage.Uniform | SilkBufferUsage.Storage | SilkBufferUsage.Upload);
            Vertices.Write(MemoryMarshal.AsBytes<float>(
            [
                -3.0f, -3.0f, 0, 0, 1, 0,
                 3.0f,  3.0f, 0, 0, 0, 1,
                 0.0f,  0.75f, 0, 0, 0, 1,
                -3.0f,  3.0f, 0, 0, 1, 1,
                -0.75f, -0.75f, 0, 0, 0, 1,
                 0.75f, -0.75f, 0, 0, 0, 1
            ]));
            SurfaceConstants = CreateSurfaceConstants(device);
            FrameConstants = CreateFrameConstants(device);
            Indices.Write(MemoryMarshal.AsBytes<uint>([4, 5, 2]));
            Uniforms.Write(MemoryMarshal.AsBytes<float>(
            [
                0.5f, 0, 0, 0,
                0, 0.75f, 0, 0,
                0, 0, 1, 0,
                0.25f, -0.125f, 0, 1,
                1, 1, 1, 1
            ]));
        }

        internal ISilkGraphicsTexture Color { get; }

        internal ISilkGraphicsTexture Depth { get; }

        internal ISilkGraphicsShaderModule VertexShader { get; }

        internal ISilkGraphicsShaderModule FragmentShader { get; }

        internal ISilkGraphicsBindingLayout BindingLayout { get; }

        internal ISilkGraphicsShaderProgram Program { get; }

        internal ISilkGraphicsPipeline Pipeline { get; }

        internal ISilkGraphicsBuffer Vertices { get; }

        internal ISilkGraphicsBuffer Indices { get; }

        internal ISilkGraphicsBuffer Uniforms { get; }



        internal ISilkGraphicsBuffer SurfaceConstants { get; }

        internal ISilkGraphicsBuffer FrameConstants { get; }

        public void Dispose()
        {
            FrameConstants.Dispose();
            SurfaceConstants.Dispose();
            Uniforms.Dispose();
            Indices.Dispose();
            Vertices.Dispose();
            Pipeline.Dispose();
            Program.Dispose();
            BindingLayout.Dispose();
            FragmentShader.Dispose();
            VertexShader.Dispose();
            Depth.Dispose();
            Color.Dispose();
        }
    }

    private sealed class ComputeResources : IDisposable
    {
        private readonly ISilkGraphicsDevice _device;
        internal ComputeResources(
            ISilkGraphicsDevice device,
            SilkShaderBinaryFormat shaderFormat,
            uint elementCount,
            SilkBufferUsage outputUsage = SilkBufferUsage.Storage,
            float fillScale = 1.5f)
        {
            _device = device;
            Output = device.CreateBuffer(
                checked((nuint)elementCount * 16),
                outputUsage);
            FillUniform = CreateUniform(elementCount, fillScale);
            ScaleUniform = CreateUniform(elementCount, 2);
            Layout = device.CreateComputeBindingLayout(
                SilkComputeBindingLayoutDescriptor.Checked);
            FillShader = device.CreateShaderModule(
                SilkCheckedShaderAssets.LoadComputeFill(shaderFormat));
            ScaleShader = device.CreateShaderModule(
                SilkCheckedShaderAssets.LoadComputeScale(shaderFormat));
            FillProgram = device.CreateComputeShaderProgram(
                new SilkComputeShaderProgramDescriptor(FillShader, Layout));
            ScaleProgram = device.CreateComputeShaderProgram(
                new SilkComputeShaderProgramDescriptor(ScaleShader, Layout));
            FillPipeline = device.CreateComputePipeline(
                SilkComputePipelineDescriptor.Checked(FillProgram));
            ScalePipeline = device.CreateComputePipeline(
                SilkComputePipelineDescriptor.Checked(ScaleProgram));
        }

        internal ISilkGraphicsBuffer Output { get; }

        internal ISilkGraphicsBuffer FillUniform { get; }

        internal ISilkComputePipeline FillPipeline { get; }

        private ISilkGraphicsBuffer ScaleUniform { get; }

        private ISilkComputeBindingLayout Layout { get; }

        private ISilkGraphicsShaderModule FillShader { get; }

        private ISilkGraphicsShaderModule ScaleShader { get; }

        private ISilkComputeShaderProgram FillProgram { get; }

        private ISilkComputeShaderProgram ScaleProgram { get; }

        private ISilkComputePipeline ScalePipeline { get; }

        internal void RecordFill(ISilkGraphicsCommandList commands, uint elementCount)
        {
            commands.SetComputePipeline(FillPipeline);
            commands.SetStorageBuffer(0, 0, Output);
            commands.SetComputeUniformBuffer(0, 1, FillUniform);
            commands.Dispatch(elementCount);
        }

        internal void RecordScale(ISilkGraphicsCommandList commands, uint elementCount)
        {
            commands.SetComputePipeline(ScalePipeline);
            commands.SetStorageBuffer(0, 0, Output);
            commands.SetComputeUniformBuffer(0, 1, ScaleUniform);
            commands.Dispatch(elementCount);
        }

        internal void RecordFillAndScale(
            ISilkGraphicsCommandList commands,
            uint elementCount)
        {
            RecordFill(commands, elementCount);
            commands.BufferBarrier(Output);
            RecordScale(commands, elementCount);
            commands.BufferBarrier(Output);
        }

        public void Dispose()
        {
            ScalePipeline.Dispose();
            FillPipeline.Dispose();
            ScaleProgram.Dispose();
            FillProgram.Dispose();
            ScaleShader.Dispose();
            FillShader.Dispose();
            Layout.Dispose();
            ScaleUniform.Dispose();
            FillUniform.Dispose();
            Output.Dispose();
        }

        private ISilkGraphicsBuffer CreateUniform(uint elementCount, float scale)
        {
            byte[] bytes = new SilkComputeParameters(elementCount, scale)
                .ToBytes(_device.Backend);
            ISilkGraphicsBuffer buffer = _device.CreateBuffer(
                checked((nuint)bytes.Length),
                SilkBufferUsage.Uniform | SilkBufferUsage.Storage | SilkBufferUsage.Upload);
            buffer.Write(bytes);
            return buffer;
        }
    }

    private static byte[] CreatePattern(int width, int height, byte seed)
    {
        var data = new byte[checked(width * height * 4)];
        for (int pixel = 0; pixel < width * height; pixel++)
        {
            int offset = pixel * 4;
            data[offset] = unchecked((byte)(seed + pixel * 3));
            data[offset + 1] = unchecked((byte)(seed + pixel * 5 + 1));
            data[offset + 2] = unchecked((byte)(seed + pixel * 7 + 2));
            data[offset + 3] = unchecked((byte)(byte.MaxValue - pixel));
        }
        return data;
    }
}
