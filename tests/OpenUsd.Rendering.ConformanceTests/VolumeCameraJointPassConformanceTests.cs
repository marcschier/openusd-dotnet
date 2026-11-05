// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class VolumeCameraJointPassConformanceTests
{
    private const uint Size = 64;
    private const string MaterialA = "/World/JointCamera/MaterialA";
    private const string MaterialB = "/World/JointCamera/MaterialB";
    private const string MaterialC = "/World/JointCamera/MaterialC";
    private const string MeshA = "/World/JointCamera/VolumeA";
    private const string MeshB = "/World/JointCamera/VolumeB";
    private const string MeshC = "/World/JointCamera/VolumeC";

    [Test]
    public async Task ColorReferenceRejectsNonFinitePixelValues()
    {
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            for (int channel = 0; channel < 3; channel++)
            {
                var actual = new double[3];
                actual[channel] = value;
                await Assert.That(() => AssertColor(actual, [0, 0, 0]))
                    .Throws<InvalidOperationException>();
            }
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false, 1e-37f)]
    [Arguments(SilkGraphicsBackend.D3D12, false, 1e-20f)]
    [Arguments(SilkGraphicsBackend.D3D12, false, 1e-8f)]
    [Arguments(SilkGraphicsBackend.D3D12, false, 1f)]
    [Arguments(SilkGraphicsBackend.D3D12, false, 1e30f)]
    [Arguments(SilkGraphicsBackend.D3D12, true, 1e-37f)]
    [Arguments(SilkGraphicsBackend.D3D12, true, 1e-20f)]
    [Arguments(SilkGraphicsBackend.D3D12, true, 1e-8f)]
    [Arguments(SilkGraphicsBackend.D3D12, true, 1f)]
    [Arguments(SilkGraphicsBackend.D3D12, true, 1e30f)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, 1e-37f)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, 1e-20f)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, 1e-8f)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, 1f)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, 1e30f)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, 1e-37f)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, 1e-20f)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, 1e-8f)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, 1f)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, 1e30f)]
    [Arguments(SilkGraphicsBackend.Metal, false, 1e-37f)]
    [Arguments(SilkGraphicsBackend.Metal, false, 1e-20f)]
    [Arguments(SilkGraphicsBackend.Metal, false, 1e-8f)]
    [Arguments(SilkGraphicsBackend.Metal, false, 1f)]
    [Arguments(SilkGraphicsBackend.Metal, false, 1e30f)]
    [Arguments(SilkGraphicsBackend.Metal, true, 1e-37f)]
    [Arguments(SilkGraphicsBackend.Metal, true, 1e-20f)]
    [Arguments(SilkGraphicsBackend.Metal, true, 1e-8f)]
    [Arguments(SilkGraphicsBackend.Metal, true, 1f)]
    [Arguments(SilkGraphicsBackend.Metal, true, 1e30f)]
    public async Task OverlappingHighFiniteTintsMatchTheIndependentComposite(
        SilkGraphicsBackend backend,
        bool sampled,
        float density)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);
        string grid = sampled
            ? VolumeCommandAuthoring.WriteDensityGrid(
                Path.Combine(AppContext.BaseDirectory, "TestResults", "joint-volumes"),
                $"high-tint-overlap-{backend}-{Guid.NewGuid():N}",
                2, 2, 2, static (_, _, _) => 1)
            : string.Empty;
        const float tint = 2e38f;
        using OpenUsdSilkPage page = Page(
            1,
            Frame(SilkMeshRendererConformance.Identity()),
            Material(MaterialA, density, [tint, 0, 0], grid, sampled, false, "2,2,2"),
            Material(MaterialB, density, [tint, 0, 0], grid, sampled, false, "2,2,2"),
            Mesh(MeshA, MaterialA, -1, 1, 1),
            Mesh(MeshB, MaterialB, -1, 1, 2));
        _ = renderer.ApplyAndRender(page, color, depth);
        float[] pixels = ReadPixels(color);
        int center = checked(((int)(Size / 2) * (int)Size + (int)(Size / 2)) * 4);
        double depthIntegral = 2 * (double)density;
        double absorbed = depthIntegral < 1e-4
            ? depthIntegral * (1 - depthIntegral / 2)
            : 1 - Math.Exp(-depthIntegral);
        double expected = tint * absorbed;
        int firstNonFinite = Array.FindIndex(pixels, static component => !float.IsFinite(component));
        string nonFiniteDescription = firstNonFinite < 0
            ? "No non-finite pixel components."
            : $"Component {firstNonFinite} (pixel {firstNonFinite / 4}, channel {firstNonFinite % 4}) " +
              $"was {pixels[firstNonFinite]:R}.";
        await Assert.That(pixels.All(float.IsFinite)).IsTrue().Because(nonFiniteDescription);
        await Assert.That(Math.Abs((pixels[center] / expected) - 1)).IsLessThan(0.01)
            .Because(
                $"Density {density:R} (sampled: {sampled}) produced red {pixels[center]:R}; " +
                $"expected {expected:R}.");
        await Assert.That(pixels[center + 1]).IsEqualTo(0f);
        await Assert.That(pixels[center + 2]).IsEqualTo(0f);
    }

    [Test]
    [MethodDataSource(nameof(AvailableBackends))]
    public Task SingleHighTintFieldProducesFinitePixels(SilkGraphicsBackend backend) =>
        AssertCoincidentHighTintFields(backend, [0.3663415014743805f]);

    [Test]
    [MethodDataSource(nameof(AvailableBackends))]
    public Task TwoCoincidentHighTintFieldsProduceFinitePixels(SilkGraphicsBackend backend) =>
        AssertCoincidentHighTintFields(backend, [0.3663415014743805f, 31.201824188232422f]);

    [Test]
    [MethodDataSource(nameof(AvailableBackends))]
    public Task EightCoincidentHighTintFieldsProduceFinitePixels(SilkGraphicsBackend backend) =>
        AssertCoincidentHighTintFields(backend,
        [
            0.3663415014743805f,
            5.4471116065979f,
            31.201824188232422f,
            14.238466262817383f,
            29.368383407592773f,
            12.753426551818848f,
            22.81144142150879f,
            21.9613094329834f
        ]);

    public static IEnumerable<SilkGraphicsBackend> AvailableBackends() =>
        SilkConformanceBackends.Available();

    private static async Task AssertCoincidentHighTintFields(
        SilkGraphicsBackend backend, float[] densities)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);
        var commands = new byte[1 + (2 * densities.Length)][];
        commands[0] = Frame(SilkMeshRendererConformance.Identity());
        for (int field = 0; field < densities.Length; field++)
        {
            string material = $"/World/JointCamera/ExtremeMaterial{field}";
            string mesh = $"/World/JointCamera/ExtremeVolume{field}";
            commands[1 + field] = Material(
                material, densities[field], [float.MaxValue, 0, 0], string.Empty, false, false);
            commands[1 + densities.Length + field] = Mesh(
                mesh, material, -1, 1, checked((ulong)field + 1));
        }

        using OpenUsdSilkPage page = Page(1, commands);
        _ = renderer.ApplyAndRender(page, color, depth);
        float[] pixels = ReadPixels(color);
        int center = checked(((int)(Size / 2) * (int)Size + (int)(Size / 2)) * 4);
        int firstNonFinite = Array.FindIndex(pixels, static component => !float.IsFinite(component));
        await Assert.That(firstNonFinite).IsEqualTo(-1)
            .Because(firstNonFinite < 0
                ? "All pixel components are finite."
                : $"Pixel component {firstNonFinite} was {pixels[firstNonFinite]:R}.");
        double depthIntegral = densities.Sum(static density => (double)density);
        double expected = float.MaxValue * (1 - Math.Exp(-depthIntegral));
        await Assert.That(Math.Abs((pixels[center] / expected) - 1))
            .IsLessThan(0.01);
        await Assert.That(pixels[center + 1]).IsEqualTo(0f);
        await Assert.That(pixels[center + 2]).IsEqualTo(0f);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false, LowDensitySignal.SubnormalScale)]
    [Arguments(SilkGraphicsBackend.D3D12, false, LowDensitySignal.StepUnderflow)]
    [Arguments(SilkGraphicsBackend.D3D12, false, LowDensitySignal.OpacityProduct)]
    [Arguments(SilkGraphicsBackend.D3D12, true, LowDensitySignal.SubnormalScale)]
    [Arguments(SilkGraphicsBackend.D3D12, true, LowDensitySignal.StepUnderflow)]
    [Arguments(SilkGraphicsBackend.D3D12, true, LowDensitySignal.OpacityProduct)]
    [Arguments(SilkGraphicsBackend.D3D12, true, LowDensitySignal.SubnormalSample)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, LowDensitySignal.SubnormalScale)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, LowDensitySignal.StepUnderflow)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, LowDensitySignal.OpacityProduct)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, LowDensitySignal.SubnormalScale)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, LowDensitySignal.StepUnderflow)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, LowDensitySignal.OpacityProduct)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, LowDensitySignal.SubnormalSample)]
    [Arguments(SilkGraphicsBackend.Metal, false, LowDensitySignal.SubnormalScale)]
    [Arguments(SilkGraphicsBackend.Metal, false, LowDensitySignal.StepUnderflow)]
    [Arguments(SilkGraphicsBackend.Metal, false, LowDensitySignal.OpacityProduct)]
    [Arguments(SilkGraphicsBackend.Metal, true, LowDensitySignal.SubnormalScale)]
    [Arguments(SilkGraphicsBackend.Metal, true, LowDensitySignal.StepUnderflow)]
    [Arguments(SilkGraphicsBackend.Metal, true, LowDensitySignal.OpacityProduct)]
    [Arguments(SilkGraphicsBackend.Metal, true, LowDensitySignal.SubnormalSample)]
    public async Task UnderflowingVolumeSignalRefusesPublicationAndAllowsRetry(
        SilkGraphicsBackend backend,
        bool sampled,
        LowDensitySignal signal)
    {
        using ISilkGraphicsDevice inner = SilkDepthCaptureConformance.CreateDevice(backend);
        using var device = new InjectableVolumeDevice(inner);
        new SilkGpuBufferBudget(16UL * 1024 * 1024).ConfigureDevice(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);
        string directory = Path.Combine(Path.GetTempPath(), $"openusd-volume-signal-{Guid.NewGuid():N}");
        string grid = sampled
            ? VolumeCommandAuthoring.WriteDensityGrid(directory, "normal", 2, 2, 2, static (_, _, _) => 1)
            : string.Empty;
        string subnormalGrid = signal == LowDensitySignal.SubnormalSample
            ? VolumeCommandAuthoring.WriteDensityGrid(directory, "subnormal", 2, 2, 2, static (_, _, _) => 1e-39f)
            : string.Empty;
        try
        {
            using OpenUsdSilkPage initial = Page(
                1,
                Frame(SilkMeshRendererConformance.Identity()),
                Material(MaterialA, 1, [1, 0, 0], grid, sampled, false, "2,2,2"),
                Mesh(MeshA, MaterialA, -1, 1, 1));
            _ = renderer.ApplyAndRender(initial, color, depth);
            float[] publishedPixels = ReadPixels(color);
            int buffers = device.CreatedBuffers;
            int textures = device.CreatedTextures.Count;
            int atlases = device.CreatedVolumeTextures;
            (float density, float opacity) = signal switch
            {
                LowDensitySignal.SubnormalScale => (1e-38f, 1),
                LowDensitySignal.StepUnderflow => (2e-38f, 1),
                LowDensitySignal.OpacityProduct => (1e-10f, 1e-30f),
                LowDensitySignal.SubnormalSample => (1e30f, 1),
                _ => throw new ArgumentOutOfRangeException(nameof(signal))
            };
            using OpenUsdSilkPage invalid = Page(
                2,
                Material(MaterialA, density, [2e38f, 0, 0],
                    signal == LowDensitySignal.SubnormalSample ? subnormalGrid : grid,
                    sampled, false, "2,2,2", opacity));
            await Assert.That(() => renderer.ApplyAndRender(invalid, color, depth))
                .Throws<InvalidDataException>().WithMessageContaining("too small");
            await Assert.That(renderer.Scene.Revision).IsEqualTo(1UL);
            await Assert.That(device.CreatedBuffers).IsEqualTo(buffers);
            await Assert.That(device.CreatedTextures.Count).IsEqualTo(textures);
            await Assert.That(device.CreatedVolumeTextures).IsEqualTo(atlases);
            await Assert.That(ReadPixels(color).AsSpan().SequenceEqual(publishedPixels)).IsTrue();

            using OpenUsdSilkPage corrected = Page(
                2,
                Material(MaterialA, 1, [0, 1, 0], grid, sampled, false, "2,2,2"));
            _ = renderer.ApplyAndRender(corrected, color, depth);
            await Assert.That(renderer.Scene.Revision).IsEqualTo(2UL);
            float[] correctedPixels = ReadPixels(color);
            int center = checked(((int)(Size / 2) * (int)Size + (int)(Size / 2)) * 4);
            await Assert.That(correctedPixels[center]).IsEqualTo(0f);
            await Assert.That(MathF.Abs(correctedPixels[center + 1] - (1 - MathF.Exp(-1))))
                .IsLessThan(0.01f);
        }
        finally
        {
            if (grid.Length != 0)
            {
                File.Delete(grid);
            }
            if (subnormalGrid.Length != 0)
            {
                File.Delete(subnormalGrid);
            }
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory);
            }
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    [Arguments(SilkGraphicsBackend.Metal, false)]
    [Arguments(SilkGraphicsBackend.Metal, true)]
    public async Task RetainedUnderflowSignalRefusesBeforeResizedOutputAndAllowsRetry(
        SilkGraphicsBackend backend,
        bool sampled)
    {
        using ISilkGraphicsDevice inner = SilkDepthCaptureConformance.CreateDevice(backend);
        using var device = new InjectableVolumeDevice(inner);
        new SilkGpuBufferBudget(16UL * 1024 * 1024).ConfigureDevice(device);
        using ISilkGraphicsTexture originalColor = device.CreateTexture2D(new SilkTextureDescriptor(
            Size / 2, Size / 2, SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture originalDepth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size / 2, Size / 2));
        using ISilkGraphicsTexture resizedColor = device.CreateTexture2D(new SilkTextureDescriptor(
            Size, Size, SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture resizedDepth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);
        string directory = Path.Combine(Path.GetTempPath(), $"openusd-volume-retained-signal-{Guid.NewGuid():N}");
        string grid = sampled
            ? VolumeCommandAuthoring.WriteDensityGrid(directory, "normal", 2, 2, 2, static (_, _, _) => 1)
            : string.Empty;
        try
        {
            using OpenUsdSilkPage initial = Page(
                1,
                Frame(SilkMeshRendererConformance.Identity()),
                Material(MaterialA, 1e-38f, [2e38f, 0, 0], grid, sampled, false, "2,2,2"),
                Mesh(MeshA, MaterialA, -1, 1, 1));
            SilkMeshRenderOptions inactive = SilkMeshRenderOptions.Default with { UseSceneMaterials = false };
            _ = renderer.ApplyAndRender(initial, originalColor, originalDepth, inactive);
            var originalPixels = new float[Size / 2 * Size / 2 * 4];
            var originalDepthPixels = new float[Size / 2 * Size / 2];
            originalColor.ReadbackForTesting(originalPixels);
            originalDepth.ReadbackForTesting(originalDepthPixels);

            using (ISilkGraphicsCommandList commands = device.CreateCommandList())
            {
                commands.ClearColor(resizedColor, new SilkColor(0.25f, 0.5f, 0.75f, 1));
                commands.ClearDepth(resizedDepth, 0.75f);
                using ISilkGraphicsSubmission submission = device.Submit(commands);
                submission.Wait();
            }
            float[] untouchedPixels = ReadPixels(resizedColor);
            var untouchedDepthPixels = new float[Size * Size];
            resizedDepth.ReadbackForTesting(untouchedDepthPixels);
            int buffers = device.CreatedBuffers;
            int textures = device.CreatedTextures.Count;
            int atlases = device.CreatedVolumeTextures;
            await Assert.That(() => renderer.Render(originalColor, originalDepth, SilkMeshRenderOptions.Default))
                .Throws<InvalidDataException>().WithMessageContaining("too small");
            await Assert.That(device.CreatedBuffers).IsEqualTo(buffers);
            await Assert.That(device.CreatedTextures.Count).IsEqualTo(textures);
            await Assert.That(device.CreatedVolumeTextures).IsEqualTo(atlases);
            await Assert.That(() => renderer.Render(resizedColor, resizedDepth, SilkMeshRenderOptions.Default))
                .Throws<InvalidDataException>().WithMessageContaining("too small");
            await Assert.That(renderer.Scene.Revision).IsEqualTo(1UL);
            await Assert.That(ReadPixels(resizedColor).AsSpan().SequenceEqual(untouchedPixels)).IsTrue();
            var afterResizedDepth = new float[untouchedDepthPixels.Length];
            resizedDepth.ReadbackForTesting(afterResizedDepth);
            await Assert.That(afterResizedDepth.AsSpan().SequenceEqual(untouchedDepthPixels)).IsTrue();
            var afterOriginal = new float[originalPixels.Length];
            var afterOriginalDepth = new float[originalDepthPixels.Length];
            originalColor.ReadbackForTesting(afterOriginal);
            originalDepth.ReadbackForTesting(afterOriginalDepth);
            await Assert.That(afterOriginal.AsSpan().SequenceEqual(originalPixels)).IsTrue();
            await Assert.That(afterOriginalDepth.AsSpan().SequenceEqual(originalDepthPixels)).IsTrue();
            await Assert.That(device.CreatedBuffers).IsEqualTo(buffers);
            await Assert.That(device.CreatedTextures.Count).IsEqualTo(textures);
            await Assert.That(device.CreatedVolumeTextures).IsEqualTo(atlases);

            _ = renderer.Render(originalColor, originalDepth, inactive);
            originalColor.ReadbackForTesting(afterOriginal);
            await Assert.That(afterOriginal.AsSpan().SequenceEqual(originalPixels)).IsTrue();
            using OpenUsdSilkPage corrected = Page(
                2, Material(MaterialA, 1, [0, 1, 0], grid, sampled, false, "2,2,2"));
            _ = renderer.ApplyAndRender(corrected, resizedColor, resizedDepth);
            await Assert.That(renderer.Scene.Revision).IsEqualTo(2UL);
            float[] correctedPixels = ReadPixels(resizedColor);
            int center = checked(((int)(Size / 2) * (int)Size + (int)(Size / 2)) * 4);
            await Assert.That(correctedPixels[center]).IsEqualTo(0f);
            await Assert.That(MathF.Abs(correctedPixels[center + 1] - (1 - MathF.Exp(-1))))
                .IsLessThan(0.01f);
        }
        finally
        {
            if (grid.Length != 0)
            {
                File.Delete(grid);
            }
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory);
            }
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeFrameFailure.OutputAllocation, false)]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeFrameFailure.FrameSubmission, false)]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeFrameFailure.OutputPublication, false)]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeFrameFailure.OutputAllocation, true)]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeFrameFailure.FrameSubmission, true)]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeFrameFailure.OutputPublication, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeFrameFailure.OutputAllocation, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeFrameFailure.FrameSubmission, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeFrameFailure.OutputPublication, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeFrameFailure.OutputAllocation, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeFrameFailure.FrameSubmission, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeFrameFailure.OutputPublication, true)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeFrameFailure.OutputAllocation, false)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeFrameFailure.FrameSubmission, false)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeFrameFailure.OutputPublication, false)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeFrameFailure.OutputAllocation, true)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeFrameFailure.FrameSubmission, true)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeFrameFailure.OutputPublication, true)]
    public async Task VolumeCameraCandidateRollsBackOnOutputFailureAndRetries(
        SilkGraphicsBackend backend,
        VolumeFrameFailure failure,
        bool staged)
    {
        using ISilkGraphicsDevice inner = SilkDepthCaptureConformance.CreateDevice(backend);
        using var device = new InjectableVolumeDevice(inner);
        var bufferBudget = new SilkGpuBufferBudget(16UL * 1024 * 1024);
        var textureBudget = new SilkGpuTextureBudget(64UL * 1024 * 1024);
        var cpuBudget = new SilkCpuTextureBudget(64UL * 1024 * 1024);
        bufferBudget.ConfigureDevice(device);
        textureBudget.ConfigureDevice(device);
        cpuBudget.ConfigureDevice(device);
        using ISilkGraphicsTexture originalColor = device.CreateTexture2D(new SilkTextureDescriptor(
            Size / 2, Size / 2, SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture originalDepth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size / 2, Size / 2));
        using ISilkGraphicsTexture resizedColor = device.CreateTexture2D(new SilkTextureDescriptor(
            Size, Size, SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture resizedDepth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device, new SilkTextureResidencyOptions(), cpuBudget);
        string directory = Path.Combine(Path.GetTempPath(), $"openusd-volume-retained-rollback-{Guid.NewGuid():N}");
        string grid = VolumeCommandAuthoring.WriteDensityGrid(
            directory, "normal", 2, 2, 2, static (_, _, _) => 1);
        string movedGrid = grid + ".moved";
        try
        {
            using OpenUsdSilkPage initial = Page(
                1,
                Frame(SilkMeshRendererConformance.Identity()),
                Material(MaterialA, 1, [1, 0, 0], grid, true, false, "2,2,2"),
                Mesh(MeshA, MaterialA, -1, 1, 1));
            _ = renderer.ApplyAndRender(initial, originalColor, originalDepth);
            var publishedPixels = new float[Size / 2 * Size / 2 * 4];
            originalColor.ReadbackForTesting(publishedPixels);
            await Assert.That(device.CreatedVolumeTextures).IsEqualTo(1);
            using (ISilkGraphicsCommandList commands = device.CreateCommandList())
            {
                commands.ClearColor(resizedColor, new SilkColor(0.25f, 0.5f, 0.75f, 1));
                commands.ClearDepth(resizedDepth, 0.75f);
                using ISilkGraphicsSubmission submission = device.Submit(commands);
                submission.Wait();
            }
            float[] untouchedPixels = ReadPixels(resizedColor);
            var untouchedDepth = new float[Size * Size];
            resizedDepth.ReadbackForTesting(untouchedDepth);
            ulong bufferBytes = bufferBudget.Usage.ReservedBytes;
            ulong textureBytes = textureBudget.Usage.ReservedBytes;
            ulong cpuBytes = cpuBudget.Usage.ReservedBytes;
            int createdBuffers = device.CreatedBuffers;
            File.Move(grid, movedGrid);
            using OpenUsdSilkPage next = Page(2, Frame(SilkMeshRendererConformance.Identity()));
            SilkMeshRenderResult renderAttempt() => staged
                ? renderer.ApplyAndRender(next, resizedColor, resizedDepth)
                : renderer.Render(resizedColor, resizedDepth);

            try
            {
                switch (failure)
                {
                    case VolumeFrameFailure.OutputAllocation:
                        device.RefuseTextureAfter = 1;
                        await Assert.That(renderAttempt)
                            .Throws<InvalidOperationException>()
                            .WithMessageContaining("Injected GPU texture preparation refusal");
                        break;
                    case VolumeFrameFailure.FrameSubmission:
                    case VolumeFrameFailure.OutputPublication:
                        device.RefuseSubmissionAfter = failure == VolumeFrameFailure.FrameSubmission ? 0 : 1;
                        await Assert.That(renderAttempt)
                            .Throws<SilkGpuStagingBudgetExceededException>();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(failure));
                }
            }
            finally
            {
                device.RefuseTextureAfter = null;
                device.RefuseSubmissionAfter = null;
            }

            await Assert.That(device.CreatedBuffers).IsGreaterThan(createdBuffers);
            await Assert.That(device.CreatedVolumeTextures).IsEqualTo(1);
            await Assert.That(renderer.Scene.Revision).IsEqualTo(1UL);
            await Assert.That(bufferBudget.Usage.ReservedBytes).IsEqualTo(bufferBytes);
            await Assert.That(textureBudget.Usage.ReservedBytes).IsEqualTo(textureBytes);
            await Assert.That(cpuBudget.Usage.ReservedBytes).IsEqualTo(cpuBytes);
            await Assert.That(ReadPixels(resizedColor).AsSpan().SequenceEqual(untouchedPixels)).IsTrue();
            var afterDepth = new float[untouchedDepth.Length];
            resizedDepth.ReadbackForTesting(afterDepth);
            await Assert.That(afterDepth.AsSpan().SequenceEqual(untouchedDepth)).IsTrue();

            _ = renderer.Render(originalColor, originalDepth);
            var retainedPixels = new float[publishedPixels.Length];
            originalColor.ReadbackForTesting(retainedPixels);
            await Assert.That(retainedPixels.AsSpan().SequenceEqual(publishedPixels)).IsTrue();
            _ = renderAttempt();
            await Assert.That(renderer.Scene.Revision).IsEqualTo(staged ? 2UL : 1UL);
            float[] retriedPixels = ReadPixels(resizedColor);
            int center = checked(((int)(Size / 2) * (int)Size + (int)(Size / 2)) * 4);
            await Assert.That(MathF.Abs(retriedPixels[center] - (1 - MathF.Exp(-1))))
                .IsLessThan(0.01f);
            await Assert.That(device.CreatedVolumeTextures).IsEqualTo(1);
        }
        finally
        {
            if (File.Exists(movedGrid))
            {
                File.Delete(movedGrid);
            }
            if (File.Exists(grid))
            {
                File.Delete(grid);
            }
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory);
            }
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeCleanupFailure.None, false)]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeCleanupFailure.DepthSnapshot, false)]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeCleanupFailure.VolumeResources, false)]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeCleanupFailure.Both, false)]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeCleanupFailure.None, true)]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeCleanupFailure.DepthSnapshot, true)]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeCleanupFailure.VolumeResources, true)]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeCleanupFailure.Both, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeCleanupFailure.None, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeCleanupFailure.DepthSnapshot, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeCleanupFailure.VolumeResources, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeCleanupFailure.Both, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeCleanupFailure.None, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeCleanupFailure.DepthSnapshot, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeCleanupFailure.VolumeResources, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeCleanupFailure.Both, true)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeCleanupFailure.None, false)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeCleanupFailure.DepthSnapshot, false)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeCleanupFailure.VolumeResources, false)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeCleanupFailure.Both, false)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeCleanupFailure.None, true)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeCleanupFailure.DepthSnapshot, true)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeCleanupFailure.VolumeResources, true)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeCleanupFailure.Both, true)]
    public async Task PublishedVolumeFrameSurvivesRetirementFailure(
        SilkGraphicsBackend backend,
        VolumeCleanupFailure failure,
        bool staged)
    {
        using ISilkGraphicsDevice inner = SilkDepthCaptureConformance.CreateDevice(backend);
        using var device = new InjectableVolumeDevice(inner);
        var bufferBudget = new SilkGpuBufferBudget(16UL * 1024 * 1024);
        bufferBudget.ConfigureDevice(device);
        using ISilkGraphicsTexture originalColor = device.CreateTexture2D(new SilkTextureDescriptor(
            Size / 2, Size / 2, SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture originalDepth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size / 2, Size / 2));
        using ISilkGraphicsTexture resizedColor = device.CreateTexture2D(new SilkTextureDescriptor(
            Size, Size, SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture resizedDepth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);
        string directory = Path.Combine(Path.GetTempPath(), $"openusd-volume-cleanup-{Guid.NewGuid():N}");
        string grid = VolumeCommandAuthoring.WriteDensityGrid(
            directory, "normal", 2, 2, 2, static (_, _, _) => 1);
        string movedGrid = grid + ".moved";
        try
        {
            if (failure is VolumeCleanupFailure.DepthSnapshot or VolumeCleanupFailure.Both)
            {
                device.FailTextureDisposals.Add(SilkTextureDescriptor.SampledDepthTarget(Size / 2, Size / 2));
            }
            if (failure is VolumeCleanupFailure.VolumeResources or VolumeCleanupFailure.Both)
            {
                device.FailTextureDisposals.Add(SilkTextureDescriptor.DepthTarget(Size / 2, Size / 2));
            }
            using OpenUsdSilkPage initial = Page(
                1,
                Frame(SilkMeshRendererConformance.Identity()),
                Material(MaterialA, 1, [1, 0, 0], grid, true, false, "2,2,2"),
                Mesh(MeshA, MaterialA, -1, 1, 1));
            _ = renderer.ApplyAndRender(initial, originalColor, originalDepth);
            await Assert.That(device.FailTextureDisposals.Count).IsEqualTo(0);
            await Assert.That(device.CreatedVolumeTextures).IsEqualTo(1);

            var body = new PhysicsRenderObjectId(101, PhysicsRenderObjectKind.RigidBody);
            var bindings = new PhysicsRenderBindingTable(1);
            await Assert.That(bindings.TryBind(body, MeshA)).IsTrue();
            var overrides = new SilkPhysicsTransformOverrides(1);
            overrides.Stage(bindings, new PhysicsRenderOverrideView(
                new[] { new PhysicsRenderTransformOverride(
                    body, new UsdVec3d(0.25, 0, -1), PhysicsRenderOrientation.Identity, true) },
                revision: 1));
            renderer.PhysicsOverrides = overrides;
            File.Move(grid, movedGrid);
            using OpenUsdSilkPage next = Page(2, Frame(SilkMeshRendererConformance.Identity()));
            SilkMeshRenderResult renderAttempt() => staged
                ? renderer.ApplyAndRender(next, resizedColor, resizedDepth)
                : renderer.Render(resizedColor, resizedDepth);

            if (failure == VolumeCleanupFailure.None)
            {
                _ = renderAttempt();
            }
            else if (failure == VolumeCleanupFailure.Both)
            {
                AggregateException? reported = null;
                try
                {
                    _ = renderAttempt();
                }
                catch (AggregateException exception)
                {
                    reported = exception;
                }
                await Assert.That(reported).IsNotNull();
                var errors = reported!.Flatten().InnerExceptions;
                await Assert.That(errors.Count).IsEqualTo(2);
                await Assert.That(errors.All(error =>
                    error.Message.Contains("Injected texture cleanup failure", StringComparison.Ordinal))).IsTrue();
                await Assert.That(errors.Select(error => error.Message).Distinct().Count()).IsEqualTo(2);
            }
            else
            {
                await Assert.That(renderAttempt)
                    .Throws<InvalidOperationException>()
                    .WithMessageContaining("Injected texture cleanup failure");
            }
            await Assert.That(device.FailingTextureDisposals)
                .IsEqualTo(failure == VolumeCleanupFailure.None ? 0 : failure == VolumeCleanupFailure.Both ? 2 : 1);
            await Assert.That(renderer.Scene.Revision).IsEqualTo(staged ? 2UL : 1UL);
            await Assert.That(overrides.Contains(1)).IsTrue();
            await Assert.That(overrides.GetTransform(1)[12]).IsEqualTo(0.25);
            float[] published = ReadPixels(resizedColor);
            int center = checked(((int)(Size / 2) * (int)Size + (int)(Size / 2)) * 4);
            await Assert.That(published[center]).IsGreaterThan(0.1f);

            _ = renderer.Render(resizedColor, resizedDepth);
            await Assert.That(ReadPixels(resizedColor).AsSpan().SequenceEqual(published)).IsTrue();
            await Assert.That(device.CreatedVolumeTextures).IsEqualTo(1);
            await Assert.That(device.TextureDisposalAttempts)
                .IsEqualTo(failure == VolumeCleanupFailure.None ? 0 : failure == VolumeCleanupFailure.Both ? 4 : 2);
        }
        finally
        {
            if (File.Exists(movedGrid))
            {
                File.Delete(movedGrid);
            }
            if (File.Exists(grid))
            {
                File.Delete(grid);
            }
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory);
            }
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeCleanupFailure.None)]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeCleanupFailure.DepthSnapshot)]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeCleanupFailure.VolumeResources)]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeCleanupFailure.Both)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeCleanupFailure.None)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeCleanupFailure.DepthSnapshot)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeCleanupFailure.VolumeResources)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeCleanupFailure.Both)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeCleanupFailure.None)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeCleanupFailure.DepthSnapshot)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeCleanupFailure.VolumeResources)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeCleanupFailure.Both)]
    public async Task UnadmittedRetainedVolumeFrameSurvivesRetirementFailure(
        SilkGraphicsBackend backend,
        VolumeCleanupFailure failure)
    {
        using ISilkGraphicsDevice inner = SilkDepthCaptureConformance.CreateDevice(backend);
        using var device = new InjectableVolumeDevice(inner);
        using ISilkGraphicsTexture originalColor = device.CreateTexture2D(new SilkTextureDescriptor(
            Size / 2, Size / 2, SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture originalDepth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size / 2, Size / 2));
        using ISilkGraphicsTexture resizedColor = device.CreateTexture2D(new SilkTextureDescriptor(
            Size, Size, SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture resizedDepth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);
        string directory = Path.Combine(Path.GetTempPath(), $"openusd-volume-direct-cleanup-{Guid.NewGuid():N}");
        string grid = VolumeCommandAuthoring.WriteDensityGrid(
            directory, "normal", 2, 2, 2, static (_, _, _) => 1);
        string movedGrid = grid + ".moved";
        try
        {
            using OpenUsdSilkPage initial = Page(
                1,
                Frame(SilkMeshRendererConformance.Identity()),
                Material(MaterialA, 1, [1, 0, 0], grid, true, false, "2,2,2"),
                Mesh(MeshA, MaterialA, -1, 1, 1));
            renderer.ApplyPage(initial);
            if (failure is VolumeCleanupFailure.DepthSnapshot or VolumeCleanupFailure.Both)
            {
                device.FailTextureDisposals.Add(SilkTextureDescriptor.SampledDepthTarget(Size / 2, Size / 2));
            }
            if (failure is VolumeCleanupFailure.VolumeResources or VolumeCleanupFailure.Both)
            {
                device.FailTextureDisposals.Add(SilkTextureDescriptor.DepthTarget(Size / 2, Size / 2));
            }
            _ = renderer.Render(originalColor, originalDepth);
            await Assert.That(device.FailTextureDisposals.Count).IsEqualTo(0);
            await Assert.That(device.CreatedVolumeTextures).IsEqualTo(1);

            var body = new PhysicsRenderObjectId(101, PhysicsRenderObjectKind.RigidBody);
            var bindings = new PhysicsRenderBindingTable(1);
            await Assert.That(bindings.TryBind(body, MeshA)).IsTrue();
            var overrides = new SilkPhysicsTransformOverrides(1);
            overrides.Stage(bindings, new PhysicsRenderOverrideView(
                new[] { new PhysicsRenderTransformOverride(
                    body, new UsdVec3d(0.25, 0, -1), PhysicsRenderOrientation.Identity, true) },
                revision: 1));
            renderer.PhysicsOverrides = overrides;
            File.Move(grid, movedGrid);
            SilkMeshRenderResult renderAttempt() => renderer.Render(resizedColor, resizedDepth);

            if (failure == VolumeCleanupFailure.None)
            {
                _ = renderAttempt();
            }
            else if (failure == VolumeCleanupFailure.Both)
            {
                AggregateException? reported = null;
                try
                {
                    _ = renderAttempt();
                }
                catch (AggregateException exception)
                {
                    reported = exception;
                }
                await Assert.That(reported).IsNotNull();
                var errors = reported!.Flatten().InnerExceptions;
                await Assert.That(errors.Count).IsEqualTo(2);
                await Assert.That(errors.All(error =>
                    error.Message.Contains("Injected texture cleanup failure", StringComparison.Ordinal))).IsTrue();
                await Assert.That(errors.Select(error => error.Message).Distinct().Count()).IsEqualTo(2);
            }
            else
            {
                await Assert.That(renderAttempt)
                    .Throws<InvalidOperationException>()
                    .WithMessageContaining("Injected texture cleanup failure");
            }
            await Assert.That(device.FailingTextureDisposals)
                .IsEqualTo(failure == VolumeCleanupFailure.None ? 0 : failure == VolumeCleanupFailure.Both ? 2 : 1);
            await Assert.That(renderer.Scene.Revision).IsEqualTo(1UL);
            await Assert.That(overrides.GetTransform(1)[12]).IsEqualTo(0.25);
            float[] published = ReadPixels(resizedColor);
            int center = checked(((int)(Size / 2) * (int)Size + (int)(Size / 2)) * 4);
            await Assert.That(published[center]).IsGreaterThan(0.1f);

            _ = renderer.Render(resizedColor, resizedDepth);
            await Assert.That(ReadPixels(resizedColor).AsSpan().SequenceEqual(published)).IsTrue();
            await Assert.That(device.CreatedVolumeTextures).IsEqualTo(1);
            await Assert.That(device.TextureDisposalAttempts)
                .IsEqualTo(failure == VolumeCleanupFailure.None ? 0 : failure == VolumeCleanupFailure.Both ? 4 : 2);
        }
        finally
        {
            if (File.Exists(movedGrid))
            {
                File.Delete(movedGrid);
            }
            if (File.Exists(grid))
            {
                File.Delete(grid);
            }
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory);
            }
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    [Arguments(SilkGraphicsBackend.Metal, false)]
    [Arguments(SilkGraphicsBackend.Metal, true)]
    public async Task FailedVolumeFrameReportsOperationAndCandidateCleanupFailuresAndRetries(
        SilkGraphicsBackend backend,
        bool staged)
    {
        using ISilkGraphicsDevice inner = SilkDepthCaptureConformance.CreateDevice(backend);
        using var device = new InjectableVolumeDevice(inner);
        var bufferBudget = new SilkGpuBufferBudget(16UL * 1024 * 1024);
        bufferBudget.ConfigureDevice(device);
        using ISilkGraphicsTexture originalColor = device.CreateTexture2D(new SilkTextureDescriptor(
            Size / 2, Size / 2, SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture originalDepth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size / 2, Size / 2));
        using ISilkGraphicsTexture resizedColor = device.CreateTexture2D(new SilkTextureDescriptor(
            Size, Size, SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture resizedDepth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);
        string directory = Path.Combine(Path.GetTempPath(), $"openusd-volume-failed-frame-cleanup-{Guid.NewGuid():N}");
        string grid = VolumeCommandAuthoring.WriteDensityGrid(
            directory, "normal", 2, 2, 2, static (_, _, _) => 1);
        string movedGrid = grid + ".moved";
        try
        {
            var body = new PhysicsRenderObjectId(101, PhysicsRenderObjectKind.RigidBody);
            var bindings = new PhysicsRenderBindingTable(1);
            await Assert.That(bindings.TryBind(body, MeshA)).IsTrue();
            var overrides = new SilkPhysicsTransformOverrides(1);
            void stageTransform(double x, ulong revision) => overrides.Stage(bindings, new PhysicsRenderOverrideView(
                new[] { new PhysicsRenderTransformOverride(
                    body, new UsdVec3d(x, 0, -1), PhysicsRenderOrientation.Identity, true) },
                revision: revision));
            stageTransform(0.1, 1);
            renderer.PhysicsOverrides = overrides;
            using OpenUsdSilkPage initial = Page(
                1,
                Frame(SilkMeshRendererConformance.Identity()),
                Material(MaterialA, 1, [1, 0, 0], grid, true, false, "2,2,2"),
                Mesh(MeshA, MaterialA, -1, 1, 1));
            _ = renderer.ApplyAndRender(initial, originalColor, originalDepth);
            var publishedPixels = new float[Size / 2 * Size / 2 * 4];
            originalColor.ReadbackForTesting(publishedPixels);
            await Assert.That(overrides.GetTransform(1)[12]).IsEqualTo(0.1);
            await Assert.That(device.CreatedVolumeTextures).IsEqualTo(1);
            using (ISilkGraphicsCommandList commands = device.CreateCommandList())
            {
                commands.ClearColor(resizedColor, new SilkColor(0.25f, 0.5f, 0.75f, 1));
                commands.ClearDepth(resizedDepth, 0.75f);
                using ISilkGraphicsSubmission submission = device.Submit(commands);
                submission.Wait();
            }
            float[] untouchedPixels = ReadPixels(resizedColor);
            var untouchedDepth = new float[Size * Size];
            resizedDepth.ReadbackForTesting(untouchedDepth);

            stageTransform(0.25, 2);
            File.Move(grid, movedGrid);
            using OpenUsdSilkPage next = Page(2, Frame(SilkMeshRendererConformance.Identity()));
            SilkMeshRenderResult renderAttempt() => staged
                ? renderer.ApplyAndRender(next, resizedColor, resizedDepth)
                : renderer.Render(resizedColor, resizedDepth);
            device.FailTextureDisposals.Add(SilkTextureDescriptor.SampledDepthTarget(Size, Size));
            device.FailTextureDisposals.Add(SilkTextureDescriptor.DepthTarget(Size, Size));
            device.RefuseSubmissionAfter = 0;
            AggregateException? reported = null;
            try
            {
                _ = renderAttempt();
            }
            catch (AggregateException exception)
            {
                reported = exception;
            }
            finally
            {
                device.RefuseSubmissionAfter = null;
            }

            await Assert.That(reported).IsNotNull();
            var failures = reported!.Flatten().InnerExceptions;
            await Assert.That(failures.Count).IsEqualTo(3);
            await Assert.That(failures.Count(error => error is SilkGpuStagingBudgetExceededException)).IsEqualTo(1);
            await Assert.That(failures.Count(error =>
                error.Message.Contains("Injected texture cleanup failure", StringComparison.Ordinal))).IsEqualTo(2);
            await Assert.That(device.FailTextureDisposals.Count).IsEqualTo(0);
            await Assert.That(device.FailingTextureDisposals).IsEqualTo(2);
            await Assert.That(device.TextureDisposalAttempts).IsEqualTo(2);
            await Assert.That(renderer.Scene.Revision).IsEqualTo(1UL);
            await Assert.That(overrides.GetTransform(1)[12]).IsEqualTo(0.1);
            var afterPublished = new float[publishedPixels.Length];
            originalColor.ReadbackForTesting(afterPublished);
            await Assert.That(afterPublished.AsSpan().SequenceEqual(publishedPixels)).IsTrue();
            await Assert.That(ReadPixels(resizedColor).AsSpan().SequenceEqual(untouchedPixels)).IsTrue();
            var afterDepth = new float[untouchedDepth.Length];
            resizedDepth.ReadbackForTesting(afterDepth);
            await Assert.That(afterDepth.AsSpan().SequenceEqual(untouchedDepth)).IsTrue();

            _ = renderAttempt();
            await Assert.That(renderer.Scene.Revision).IsEqualTo(staged ? 2UL : 1UL);
            await Assert.That(overrides.GetTransform(1)[12]).IsEqualTo(0.25);
            await Assert.That(device.TextureDisposalAttempts).IsEqualTo(4);
            await Assert.That(device.CreatedVolumeTextures).IsEqualTo(1);
            float[] retriedPixels = ReadPixels(resizedColor);
            int center = checked(((int)(Size / 2) * (int)Size + (int)(Size / 2)) * 4);
            await Assert.That(retriedPixels[center]).IsGreaterThan(0.1f);
        }
        finally
        {
            if (File.Exists(movedGrid))
            {
                File.Delete(movedGrid);
            }
            if (File.Exists(grid))
            {
                File.Delete(grid);
            }
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory);
            }
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    [Arguments(SilkGraphicsBackend.Metal, false)]
    [Arguments(SilkGraphicsBackend.Metal, true)]
    public async Task SameSizePublicationFailurePreservesPublishedVolumeDepthSnapshot(
        SilkGraphicsBackend backend,
        bool staged)
    {
        using ISilkGraphicsDevice inner = SilkDepthCaptureConformance.CreateDevice(backend);
        using var device = new InjectableVolumeDevice(inner);
        var bufferBudget = new SilkGpuBufferBudget(16UL * 1024 * 1024);
        bufferBudget.ConfigureDevice(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size, Size, SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);
        using OpenUsdSilkPage initial = Page(
            1,
            Frame(SilkMeshRendererConformance.Identity()),
            Material(MaterialA, 1, [1, 0, 0], string.Empty, false, false),
            Mesh(MeshA, MaterialA, -1, 1, 1));
        _ = renderer.ApplyAndRender(initial, color, depth);
        ISilkGraphicsTexture snapshot = GetPublishedVolumeDepthSnapshot(renderer);
        var snapshotPixels = new float[Size * Size];
        snapshot.ReadbackForTesting(snapshotPixels);
        await Assert.That(snapshotPixels.All(value => value == 1)).IsTrue();
        float[] publishedPixels = ReadPixels(color);
        var publishedDepth = new float[Size * Size];
        depth.ReadbackForTesting(publishedDepth);

        using OpenUsdSilkPage next = Page(2, Frame(SilkMeshRendererConformance.Identity()));
        SilkMeshRenderOptions changedDepth = SilkMeshRenderOptions.Default with { ClearDepth = 0.5f };
        SilkMeshRenderResult renderAttempt() => staged
            ? renderer.ApplyAndRender(next, color, depth, changedDepth)
            : renderer.Render(color, depth, changedDepth);
        device.RefuseSubmissionAfter = 1;
        try
        {
            await Assert.That(renderAttempt).Throws<SilkGpuStagingBudgetExceededException>();
        }
        finally
        {
            device.RefuseSubmissionAfter = null;
        }

        await Assert.That(renderer.Scene.Revision).IsEqualTo(1UL);
        await Assert.That(ReadPixels(color).AsSpan().SequenceEqual(publishedPixels)).IsTrue();
        var afterDepth = new float[publishedDepth.Length];
        depth.ReadbackForTesting(afterDepth);
        await Assert.That(afterDepth.AsSpan().SequenceEqual(publishedDepth)).IsTrue();
        await Assert.That(ReferenceEquals(GetPublishedVolumeDepthSnapshot(renderer), snapshot)).IsTrue();
        var afterSnapshot = new float[snapshotPixels.Length];
        snapshot.ReadbackForTesting(afterSnapshot);
        await Assert.That(afterSnapshot.AsSpan().SequenceEqual(snapshotPixels)).IsTrue();

        _ = renderAttempt();
        await Assert.That(renderer.Scene.Revision).IsEqualTo(staged ? 2UL : 1UL);
        ISilkGraphicsTexture retriedSnapshot = GetPublishedVolumeDepthSnapshot(renderer);
        retriedSnapshot.ReadbackForTesting(afterSnapshot);
        await Assert.That(afterSnapshot.All(value => value == 0.5f)).IsTrue();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task AdmittedVolumeDepthSnapshotsReuseTwoBudgetedTextures(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice inner = SilkDepthCaptureConformance.CreateDevice(backend);
        using var device = new InjectableVolumeDevice(inner);
        var textureBudget = new SilkGpuTextureBudget(64UL * 1024 * 1024);
        textureBudget.ConfigureDevice(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size, Size, SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Size, Size));
        ulong externalBytes = textureBudget.Usage.ReservedBytes;
        using var renderer = new SilkMeshRenderer(device);
        using OpenUsdSilkPage initial = Page(
            1,
            Frame(SilkMeshRendererConformance.Identity()),
            Material(MaterialA, 1, [1, 0, 0], string.Empty, false, false),
            Mesh(MeshA, MaterialA, -1, 1, 1));
        _ = renderer.ApplyAndRender(initial, color, depth);
        ISilkGraphicsTexture first = GetPublishedVolumeDepthSnapshot(renderer);
        ulong firstBytes = textureBudget.Usage.ReservedBytes;

        _ = renderer.Render(color, depth);
        ISilkGraphicsTexture second = GetPublishedVolumeDepthSnapshot(renderer);
        await Assert.That(ReferenceEquals(first, second)).IsFalse();
        ulong steadyBytes = textureBudget.Usage.ReservedBytes;
        await Assert.That(steadyBytes - firstBytes).IsEqualTo((ulong)Size * Size * sizeof(float));

        _ = renderer.Render(color, depth);
        await Assert.That(ReferenceEquals(GetPublishedVolumeDepthSnapshot(renderer), first)).IsTrue();
        await Assert.That(textureBudget.Usage.ReservedBytes).IsEqualTo(steadyBytes);
        renderer.Dispose();
        await Assert.That(textureBudget.Usage.ReservedBytes).IsEqualTo(externalBytes);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false, false)]
    [Arguments(SilkGraphicsBackend.D3D12, false, true)]
    [Arguments(SilkGraphicsBackend.D3D12, true, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, true)]
    [Arguments(SilkGraphicsBackend.Metal, false, false)]
    [Arguments(SilkGraphicsBackend.Metal, false, true)]
    [Arguments(SilkGraphicsBackend.Metal, true, false)]
    [Arguments(SilkGraphicsBackend.Metal, true, true)]
    public async Task SnapshotBudgetEnforcesExactOverlapAndPreservesRefusedFrame(
        SilkGraphicsBackend backend,
        bool staged,
        bool exactBoundary)
    {
        using ISilkGraphicsDevice inner = SilkDepthCaptureConformance.CreateDevice(backend);
        using var device = new InjectableVolumeDevice(inner);
        var textureBudget = new SilkGpuTextureBudget(8UL * 1024 * 1024);
        textureBudget.ConfigureDevice(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size, Size, SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);
        using OpenUsdSilkPage initial = Page(
            1,
            Frame(SilkMeshRendererConformance.Identity()),
            Material(MaterialA, 1, [1, 0, 0], string.Empty, false, false),
            Mesh(MeshA, MaterialA, -1, 1, 1));
        _ = renderer.ApplyAndRender(initial, color, depth);
        ISilkGraphicsTexture snapshot = GetPublishedVolumeDepthSnapshot(renderer);
        var snapshotPixels = new float[Size * Size];
        snapshot.ReadbackForTesting(snapshotPixels);
        float[] publishedPixels = ReadPixels(color);
        var publishedDepth = new float[Size * Size];
        depth.ReadbackForTesting(publishedDepth);

        ulong snapshotBytes = (ulong)Size * Size * sizeof(float);
        ulong available = 2 * snapshotBytes - (exactBoundary ? 0UL : 1UL);
        ulong pressureBytes = textureBudget.MaximumBytes - textureBudget.Usage.ReservedBytes - available;
        using IDisposable pressure = textureBudget.Reserve(
            new SilkTextureDescriptor(checked((uint)pressureBytes), 1, SilkTextureFormat.R8Unorm,
                SilkTextureUsage.Sampled),
            1);
        ulong reservedUnderPressure = textureBudget.Usage.ReservedBytes;
        using OpenUsdSilkPage next = Page(2, Frame(SilkMeshRendererConformance.Identity()));
        SilkMeshRenderOptions changedDepth = SilkMeshRenderOptions.Default with { ClearDepth = 0.5f };
        SilkMeshRenderResult renderAttempt() => staged
            ? renderer.ApplyAndRender(next, color, depth, changedDepth)
            : renderer.Render(color, depth, changedDepth);

        var afterSnapshot = new float[snapshotPixels.Length];
        if (exactBoundary)
        {
            _ = renderAttempt();
            await Assert.That(textureBudget.Usage.ReservedBytes).IsEqualTo(reservedUnderPressure + snapshotBytes);
            pressure.Dispose();
        }
        else
        {
            var refusal = await Assert.That(renderAttempt).Throws<SilkGpuTextureBudgetExceededException>();
            await Assert.That(refusal!.RequestedBytes).IsEqualTo(snapshotBytes);
            await Assert.That(renderer.Scene.Revision).IsEqualTo(1UL);
            await Assert.That(ReferenceEquals(GetPublishedVolumeDepthSnapshot(renderer), snapshot)).IsTrue();
            snapshot.ReadbackForTesting(afterSnapshot);
            await Assert.That(afterSnapshot.AsSpan().SequenceEqual(snapshotPixels)).IsTrue();
            await Assert.That(ReadPixels(color).AsSpan().SequenceEqual(publishedPixels)).IsTrue();
            var afterDepth = new float[publishedDepth.Length];
            depth.ReadbackForTesting(afterDepth);
            await Assert.That(afterDepth.AsSpan().SequenceEqual(publishedDepth)).IsTrue();
            await Assert.That(textureBudget.Usage.ReservedBytes).IsEqualTo(reservedUnderPressure);
            pressure.Dispose();
            _ = renderAttempt();
        }
        await Assert.That(renderer.Scene.Revision).IsEqualTo(staged ? 2UL : 1UL);
        GetPublishedVolumeDepthSnapshot(renderer).ReadbackForTesting(afterSnapshot);
        await Assert.That(afterSnapshot.All(value => value == 0.5f)).IsTrue();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task RendererDisposalReportsVolumeCleanupFailuresAndAllowsReleaseRetry(SilkGraphicsBackend backend)
    {
        ISilkGraphicsDevice inner = SilkDepthCaptureConformance.CreateDevice(backend);
        var device = new InjectableVolumeDevice(inner);
        var bufferBudget = new SilkGpuBufferBudget(16UL * 1024 * 1024);
        bufferBudget.ConfigureDevice(device);
        ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size, Size, SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Size, Size));
        var renderer = new SilkMeshRenderer(device);
        Exception? reported = null;
        Exception? retryFailure = null;
        var teardownFailures = new List<Exception>();
        bool renderRejected = false;
        int failuresAfterFirstDispose = 0;
        int attemptsAfterFirstDispose = 0;
        int attemptsAfterRetry = 0;
        ulong bytesAfterRetry = 0;
        try
        {
            device.FailTextureDisposals.Add(SilkTextureDescriptor.SampledDepthTarget(Size, Size));
            device.FailTextureDisposals.Add(SilkTextureDescriptor.DepthTarget(Size, Size));
            using OpenUsdSilkPage initial = Page(
                1,
                Frame(SilkMeshRendererConformance.Identity()),
                Material(MaterialA, 1, [1, 0, 0], string.Empty, false, false),
                Mesh(MeshA, MaterialA, -1, 1, 1));
            _ = renderer.ApplyAndRender(initial, color, depth);
            await Assert.That(device.FailTextureDisposals.Count).IsEqualTo(0);
            try
            {
                renderer.Dispose();
            }
            catch (Exception exception)
            {
                reported = exception;
            }
            failuresAfterFirstDispose = device.FailingTextureDisposals;
            attemptsAfterFirstDispose = device.TextureDisposalAttempts;
            try
            {
                _ = renderer.Render(color, depth);
            }
            catch (ObjectDisposedException)
            {
                renderRejected = true;
            }
            try
            {
                renderer.Dispose();
            }
            catch (Exception exception)
            {
                retryFailure = exception;
            }
            attemptsAfterRetry = device.TextureDisposalAttempts;
            bytesAfterRetry = bufferBudget.Usage.ReservedBytes;
        }
        finally
        {
            foreach (IDisposable resource in new IDisposable[] { renderer, color, depth, device })
            {
                try
                {
                    resource.Dispose();
                }
                catch (Exception exception)
                {
                    teardownFailures.Add(exception);
                }
            }
        }

        await Assert.That(reported is AggregateException).IsTrue();
        var failures = ((AggregateException)reported!).Flatten().InnerExceptions;
        await Assert.That(failures.Count).IsEqualTo(2);
        await Assert.That(failures.All(error =>
            error.Message.Contains("Injected texture cleanup failure", StringComparison.Ordinal))).IsTrue();
        await Assert.That(failuresAfterFirstDispose).IsEqualTo(2);
        await Assert.That(attemptsAfterFirstDispose).IsEqualTo(2);
        await Assert.That(renderRejected).IsTrue();
        await Assert.That(retryFailure).IsNull();
        await Assert.That(attemptsAfterRetry).IsEqualTo(4);
        await Assert.That(bytesAfterRetry).IsEqualTo(0UL);
        await Assert.That(teardownFailures.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    [Arguments(SilkGraphicsBackend.Metal, false)]
    [Arguments(SilkGraphicsBackend.Metal, true)]
    public async Task UnderflowAdmissionSkipsInactiveMaterials(
        SilkGraphicsBackend backend,
        bool disableMaterials)
    {
        using ISilkGraphicsDevice inner = SilkDepthCaptureConformance.CreateDevice(backend);
        using var device = new InjectableVolumeDevice(inner);
        new SilkGpuBufferBudget(16UL * 1024 * 1024).ConfigureDevice(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);
        string missingGrid = Path.Combine(Path.GetTempPath(), $"missing-volume-{Guid.NewGuid():N}.r32");
        using OpenUsdSilkPage page = Page(
            1,
            Frame(SilkMeshRendererConformance.Identity()),
            Material(MaterialA, 2e-38f, [2e38f, 0, 0], missingGrid, true, false,
                "2,2,2", disableMaterials ? 1 : 0),
            Mesh(MeshA, MaterialA, -1, 1, 1));
        SilkMeshRenderOptions options = SilkMeshRenderOptions.Default with
        {
            UseSceneMaterials = !disableMaterials
        };
        _ = renderer.ApplyAndRender(page, color, depth, options);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1UL);
        await Assert.That(device.CreatedVolumeTextures).IsEqualTo(0);
        await Assert.That(ReadPixels(color).All(float.IsFinite)).IsTrue();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false, false)]
    [Arguments(SilkGraphicsBackend.D3D12, false, true)]
    [Arguments(SilkGraphicsBackend.D3D12, true, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, true)]
    [Arguments(SilkGraphicsBackend.Metal, false, false)]
    [Arguments(SilkGraphicsBackend.Metal, false, true)]
    [Arguments(SilkGraphicsBackend.Metal, true, false)]
    [Arguments(SilkGraphicsBackend.Metal, true, true)]
    public async Task VolumeTintExposureOverflowRefusesBeforePublicationAndRetries(
        SilkGraphicsBackend backend,
        bool sampled,
        bool replacement)
    {
        using ISilkGraphicsDevice inner = SilkDepthCaptureConformance.CreateDevice(backend);
        using var device = new InjectableVolumeDevice(inner);
        new SilkGpuBufferBudget(16UL * 1024 * 1024).ConfigureDevice(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);
        string grid = sampled
            ? VolumeCommandAuthoring.WriteDensityGrid(
                Path.Combine(AppContext.BaseDirectory, "TestResults", "joint-volumes"),
                $"exposure-density-{backend}-{replacement}-{Guid.NewGuid():N}",
                2, 2, 2, static (_, _, _) => 1)
            : string.Empty;
        using OpenUsdSilkPage initial = Page(
            1,
            Frame(SilkMeshRendererConformance.Identity()),
            Material(MaterialA, 1, [3, 0, 0], grid, sampled, false, "2,2,2"),
            Mesh(MeshA, MaterialA, -1, 1, 1));
        SilkMeshRenderOptions admitted = SilkMeshRenderOptions.Default with { Exposure = 126 };
        _ = renderer.ApplyAndRender(initial, color, depth, admitted);
        float[] publishedPixels = ReadPixels(color);
        int center = checked(((int)(Size / 2) * (int)Size + (int)(Size / 2)) * 4);
        double expected = 3 * Math.Pow(2, 126) * (1 - Math.Exp(-1));
        await Assert.That(publishedPixels.All(float.IsFinite)).IsTrue();
        await Assert.That(Math.Abs((publishedPixels[center] / expected) - 1)).IsLessThan(0.01);
        int buffers = device.CreatedBuffers;
        int textures = device.CreatedTextures.Count;
        int atlases = device.CreatedVolumeTextures;
        using OpenUsdSilkPage updated = Page(
            2,
            Material(MaterialA, 1, [0, 3, 0], grid, sampled, false, "2,2,2"));
        SilkMeshRenderOptions excessive = admitted with { Exposure = 127 };
        await Assert.That(() => replacement
                ? renderer.ApplyAndRender(updated, color, depth, excessive)
                : renderer.Render(color, depth, excessive))
            .Throws<InvalidDataException>().WithMessageContaining("tint and exposure");
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1UL);
        await Assert.That(device.CreatedBuffers).IsEqualTo(buffers);
        await Assert.That(device.CreatedTextures.Count).IsEqualTo(textures);
        await Assert.That(device.CreatedVolumeTextures).IsEqualTo(atlases);
        await Assert.That(ReadPixels(color).AsSpan().SequenceEqual(publishedPixels)).IsTrue();

        _ = replacement
            ? renderer.ApplyAndRender(updated, color, depth, admitted)
            : renderer.Render(color, depth, admitted);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(replacement ? 2UL : 1UL);
        float[] retriedPixels = ReadPixels(color);
        await Assert.That(retriedPixels.All(float.IsFinite)).IsTrue();
        int channel = replacement ? 1 : 0;
        await Assert.That(Math.Abs((retriedPixels[center + channel] / expected) - 1)).IsLessThan(0.01);
        await Assert.That(retriedPixels[center + (replacement ? 0 : 1)]).IsEqualTo(0f);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    [Arguments(SilkGraphicsBackend.Metal, false)]
    [Arguments(SilkGraphicsBackend.Metal, true)]
    public async Task VolumeTintExposureAdmissionSkipsInactiveMaterials(
        SilkGraphicsBackend backend,
        bool disableSceneMaterials)
    {
        using ISilkGraphicsDevice inner = SilkDepthCaptureConformance.CreateDevice(backend);
        using var device = new InjectableVolumeDevice(inner);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);
        string grid = VolumeCommandAuthoring.WriteDensityGrid(
            Path.Combine(AppContext.BaseDirectory, "TestResults", "joint-volumes"),
            $"inactive-exposure-{backend}-{disableSceneMaterials}-{Guid.NewGuid():N}",
            2, 2, 2, static (_, _, _) => 1);
        using OpenUsdSilkPage page = Page(
            1,
            Frame(SilkMeshRendererConformance.Identity()),
            Material(MaterialA, 1, [3, 0, 0], grid, true, false, "2,2,2",
                opacity: disableSceneMaterials ? 1 : 0),
            Mesh(MeshA, MaterialA, -1, 1, 1));
        SilkMeshRenderOptions options = SilkMeshRenderOptions.Default with
        {
            Exposure = 127,
            UseSceneMaterials = !disableSceneMaterials
        };
        _ = renderer.ApplyAndRender(page, color, depth, options);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1UL);
        await Assert.That(device.CreatedVolumeTextures).IsEqualTo(0);
        if (!disableSceneMaterials)
        {
            float[] pixels = ReadPixels(color);
            int center = checked(((int)(Size / 2) * (int)Size + (int)(Size / 2)) * 4);
            await Assert.That(pixels[center]).IsEqualTo(0f);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    [Arguments(SilkGraphicsBackend.Metal, false)]
    [Arguments(SilkGraphicsBackend.Metal, true)]
    public async Task NonfiniteDensityRefusalPreservesFrameAndSceneAndRetriesAfterCorrection(
        SilkGraphicsBackend backend,
        bool sampled)
    {
        using ISilkGraphicsDevice inner = SilkDepthCaptureConformance.CreateDevice(backend);
        using var device = new InjectableVolumeDevice(inner);
        new SilkGpuBufferBudget(16UL * 1024 * 1024).ConfigureDevice(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);
        using OpenUsdSilkPage initial = Page(
            1,
            Frame(SilkMeshRendererConformance.Identity()),
            Material(MaterialA, 1, [1, 0, 0], string.Empty, false, false),
            Mesh(MeshA, MaterialA, -1, 1, 1));
        _ = renderer.ApplyAndRender(initial, color, depth);
        float[] publishedPixels = ReadPixels(color);

        string directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "joint-volumes");
        string name = $"nonfinite-density-{backend}-{Guid.NewGuid():N}";
        string grid = sampled
            ? VolumeCommandAuthoring.WriteDensityGrid(
                directory, name, 2, 2, 2, static (x, y, z) => x == 1 && y == 0 && z == 0 ? float.NaN : 1)
            : string.Empty;
        using OpenUsdSilkPage invalid = Page(
            2,
            Material(MaterialA, sampled ? 1 : float.PositiveInfinity, [0, 1, 0],
                grid, sampled, false, "2,2,2"));
        InvalidDataException exception = (await Assert.That(
            () => renderer.ApplyAndRender(invalid, color, depth))
            .Throws<InvalidDataException>())!;
        await Assert.That(exception.Message).Contains(sampled ? "voxel 1" : "nonfinite density scalar");
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1UL);
        await Assert.That(device.CreatedVolumeTextures).IsEqualTo(0);
        await Assert.That(ReadPixels(color).AsSpan().SequenceEqual(publishedPixels)).IsTrue();
        _ = renderer.Render(color, depth);
        await Assert.That(ReadPixels(color).AsSpan().SequenceEqual(publishedPixels)).IsTrue();

        if (sampled)
        {
            _ = VolumeCommandAuthoring.WriteDensityGrid(
                directory, name, 2, 2, 2, static (_, _, _) => 1);
        }
        using OpenUsdSilkPage corrected = Page(
            2,
            Material(MaterialA, 1, [0, 1, 0], grid, sampled, false, "2,2,2"));
        _ = renderer.ApplyAndRender(corrected, color, depth);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(2UL);
        await Assert.That(device.CreatedVolumeTextures).IsEqualTo(sampled ? 1 : 0);
        float[] correctedPixels = ReadPixels(color);
        int center = checked(((int)(Size / 2) * (int)Size + (int)(Size / 2)) * 4);
        await Assert.That(correctedPixels[center + 1]).IsGreaterThan(0.1f);
        await Assert.That(correctedPixels.AsSpan().SequenceEqual(publishedPixels)).IsFalse();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    [Arguments(SilkGraphicsBackend.Metal, false)]
    [Arguments(SilkGraphicsBackend.Metal, true)]
    public async Task FiniteDensityProductRefusalPreservesFrameAndSceneAndRetriesAfterCorrection(
        SilkGraphicsBackend backend,
        bool sampled)
    {
        using ISilkGraphicsDevice inner = SilkDepthCaptureConformance.CreateDevice(backend);
        using var device = new InjectableVolumeDevice(inner);
        new SilkGpuBufferBudget(16UL * 1024 * 1024).ConfigureDevice(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);
        using OpenUsdSilkPage initial = Page(
            1,
            Frame(SilkMeshRendererConformance.Identity()),
            Material(MaterialA, 1, [1, 0, 0], string.Empty, false, false),
            Mesh(MeshA, MaterialA, -1, 1, 1));
        _ = renderer.ApplyAndRender(initial, color, depth);
        float[] publishedPixels = ReadPixels(color);

        string grid = sampled
            ? VolumeCommandAuthoring.WriteDensityGrid(
                Path.Combine(AppContext.BaseDirectory, "TestResults", "joint-volumes"),
                $"finite-density-{backend}-{Guid.NewGuid():N}",
                2, 2, 2, static (_, _, _) => 1e30f)
            : string.Empty;
        using OpenUsdSilkPage invalid = Page(
            2,
            Material(MaterialA, sampled ? 1e10f : float.MaxValue, [0, 1, 0],
                grid, sampled, false, "2,2,2"));
        await Assert.That(() => renderer.ApplyAndRender(invalid, color, depth))
            .Throws<InvalidDataException>().WithMessageContaining("density-scale arithmetic");
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1UL);
        await Assert.That(device.CreatedVolumeTextures).IsEqualTo(0);
        await Assert.That(ReadPixels(color).AsSpan().SequenceEqual(publishedPixels)).IsTrue();
        _ = renderer.Render(color, depth);
        await Assert.That(ReadPixels(color).AsSpan().SequenceEqual(publishedPixels)).IsTrue();

        using OpenUsdSilkPage corrected = Page(
            2,
            Material(MaterialA, sampled ? 1e-30f : 1, [0, 1, 0], grid, sampled, false, "2,2,2"));
        _ = renderer.ApplyAndRender(corrected, color, depth);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(2UL);
        await Assert.That(device.CreatedVolumeTextures).IsEqualTo(sampled ? 1 : 0);
        float[] correctedPixels = ReadPixels(color);
        int center = checked(((int)(Size / 2) * (int)Size + (int)(Size / 2)) * 4);
        await Assert.That(correctedPixels.All(float.IsFinite)).IsTrue();
        await Assert.That(correctedPixels[center + 1]).IsGreaterThan(0.1f);
        await Assert.That(correctedPixels.AsSpan().SequenceEqual(publishedPixels)).IsFalse();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false, 1e20f, 0.5f)]
    [Arguments(SilkGraphicsBackend.D3D12, false, 1e20f, 1f)]
    [Arguments(SilkGraphicsBackend.D3D12, false, 1e30f, 0.5f)]
    [Arguments(SilkGraphicsBackend.D3D12, false, 1e30f, 1f)]
    [Arguments(SilkGraphicsBackend.D3D12, true, 1e20f, 0.5f)]
    [Arguments(SilkGraphicsBackend.D3D12, true, 1e20f, 1f)]
    [Arguments(SilkGraphicsBackend.D3D12, true, 1e30f, 0.5f)]
    [Arguments(SilkGraphicsBackend.D3D12, true, 1e30f, 1f)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, 1e20f, 0.5f)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, 1e20f, 1f)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, 1e30f, 0.5f)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, 1e30f, 1f)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, 1e20f, 0.5f)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, 1e20f, 1f)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, 1e30f, 0.5f)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, 1e30f, 1f)]
    [Arguments(SilkGraphicsBackend.Metal, false, 1e20f, 0.5f)]
    [Arguments(SilkGraphicsBackend.Metal, false, 1e20f, 1f)]
    [Arguments(SilkGraphicsBackend.Metal, false, 1e30f, 0.5f)]
    [Arguments(SilkGraphicsBackend.Metal, false, 1e30f, 1f)]
    [Arguments(SilkGraphicsBackend.Metal, true, 1e20f, 0.5f)]
    [Arguments(SilkGraphicsBackend.Metal, true, 1e20f, 1f)]
    [Arguments(SilkGraphicsBackend.Metal, true, 1e30f, 0.5f)]
    [Arguments(SilkGraphicsBackend.Metal, true, 1e30f, 1f)]
    public async Task LargeFiniteDensityInThinFieldRemainsVisible(
        SilkGraphicsBackend backend,
        bool sampled,
        float density,
        float opacity)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);
        byte[] frame = Frame(SilkMeshRendererConformance.Identity());
        BinaryPrimitives.WriteDoubleLittleEndian(frame.AsSpan(144 + (10 * sizeof(double))), -0.5);
        BinaryPrimitives.WriteDoubleLittleEndian(frame.AsSpan(144 + (14 * sizeof(double))), -1);
        string grid = sampled
            ? VolumeCommandAuthoring.WriteDensityGrid(
                Path.Combine(AppContext.BaseDirectory, "TestResults", "joint-volumes"),
                $"thin-density-{backend}-{Guid.NewGuid():N}",
                2, 2, 2, static (_, _, _) => 1)
            : string.Empty;
        using OpenUsdSilkPage initial = Page(
            1,
            frame,
            Material(MaterialA, 1, [1, 0, 0], grid, sampled, false, "2,2,2", opacity),
            Mesh(MeshA, MaterialA, 0, 1e-10, 1));
        _ = renderer.ApplyAndRender(initial, color, depth);
        float[] ordinaryPixels = ReadPixels(color);
        int center = checked(((int)(Size / 2) * (int)Size + (int)(Size / 2)) * 4);
        float ordinaryExpected = opacity * (1 - MathF.Exp(-0.5f));
        await Assert.That(MathF.Abs(ordinaryPixels[center] - ordinaryExpected)).IsLessThan(0.01f);

        using OpenUsdSilkPage dense = Page(
            2,
            Material(MaterialA, density, [1, 0, 0], grid, sampled, false, "2,2,2", opacity));
        _ = renderer.ApplyAndRender(dense, color, depth);
        float[] densePixels = ReadPixels(color);
        int firstNonFinite = Array.FindIndex(
            densePixels,
            static component => !float.IsFinite(component));
        string nonFiniteDescription = firstNonFinite < 0
            ? "No non-finite pixel components."
            : $"Component {firstNonFinite} (pixel {firstNonFinite / 4}, channel {firstNonFinite % 4}) " +
              $"was {densePixels[firstNonFinite]:R}.";
        await Assert.That(densePixels.All(float.IsFinite)).IsTrue().Because(nonFiniteDescription);
        await Assert.That(MathF.Abs(densePixels[center] - opacity)).IsLessThan(0.01f);
        await Assert.That(densePixels[center + 1]).IsEqualTo(0f);
        await Assert.That(densePixels[center + 2]).IsEqualTo(0f);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task NestedUnequalTintVolumesMatchIndependentReferenceAcrossInsertionAndCameraOrder(
        SilkGraphicsBackend backend)
    {
        string grid = VolumeCommandAuthoring.WriteDensityGrid(
            Path.Combine(AppContext.BaseDirectory, "TestResults", "joint-volumes"),
            $"joint-camera-{backend}",
            8,
            8,
            8,
            static (_, _, _) => 1);
        double[] redTint = [1, 0, 0];
        double[] blueTint = [0, 0, 1];
        double[] greenTint = [0, 1, 0];
        (double[] forwardReference, double[] reverseReference) = ExpectedNestedColors(
            redTint,
            blueTint,
            greenTint);

        foreach (bool volumeTextureSupport in new[] { true, false })
        {
            foreach ((bool textured, bool missingGrid) in new[]
            {
                (false, false),
                (true, false),
                (false, true),
            })
            {
                if (volumeTextureSupport && missingGrid)
                {
                    continue;
                }
                double[] forward = RenderCenter(
                    backend,
                    grid,
                    textured,
                    missingGrid,
                    hideVolumeTextureSupport: !volumeTextureSupport,
                    reverseInsertion: false,
                    reverseCamera: false);
                double[] insertedBackwards = RenderCenter(
                    backend,
                    grid,
                    textured,
                    missingGrid,
                    hideVolumeTextureSupport: !volumeTextureSupport,
                    reverseInsertion: true,
                    reverseCamera: false);
                double[] reverseCamera = RenderCenter(
                    backend,
                    grid,
                    textured,
                    missingGrid,
                    hideVolumeTextureSupport: !volumeTextureSupport,
                    reverseInsertion: false,
                    reverseCamera: true);

                AssertColor(forward, forwardReference);
                AssertColor(insertedBackwards, forwardReference);
                AssertColor(reverseCamera, reverseReference);
                await Assert.That(Math.Abs(forward[0] - reverseCamera[0]))
                    .IsGreaterThan(0.05);
            }
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public Task UniformAndMixedTexturedFieldsRespectFarAndOpaqueClippingOnIntendedBackend(
        SilkGraphicsBackend backend)
    {
        string grid = VolumeCommandAuthoring.WriteDensityGrid(
            Path.Combine(AppContext.BaseDirectory, "TestResults", "joint-volumes"),
            $"joint-clipped-{backend}",
            8,
            8,
            8,
            static (_, _, _) => 1);
        foreach (bool volumeTextureSupport in new[] { true, false })
        {
            foreach (bool useFarPlane in new[] { true, false })
            {
                double distance = useFarPlane ? 0.6 : 0.7;
                double[] background = useFarPlane
                    ? [0, 0, 0]
                    : RenderFieldsAtClip(backend, grid, useFarPlane, false,
                        hideVolumeTextureSupport: !volumeTextureSupport, includeVolumes: false);
                if (!useFarPlane && (!double.IsFinite(background[1]) || background[1] <= 0))
                {
                    throw new InvalidOperationException("The opaque occluder must cover the reference pixel.");
                }
                foreach (bool mixedTextured in new[] { false, true })
                {
                    double[] expected = ExpectedClippedFieldColor(
                        distance,
                        background,
                        mixedTextured);
                    double[] actual = RenderFieldsAtClip(
                        backend,
                        grid,
                        useFarPlane,
                        mixedTextured,
                        hideVolumeTextureSupport: !volumeTextureSupport);
                    AssertColor(actual, expected);
                }
            }
        }

        return Task.CompletedTask;
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    [Arguments(SilkGraphicsBackend.Metal, false)]
    [Arguments(SilkGraphicsBackend.Metal, true)]
    public async Task StagedApplyAndRenderPublishesVolumeResourcesThatSurviveUnchangedRedraw(
        SilkGraphicsBackend backend,
        bool textured)
    {
        using ISilkGraphicsDevice inner = SilkDepthCaptureConformance.CreateDevice(backend);
        using var device = new InjectableVolumeDevice(inner);
        new SilkGpuBufferBudget(16UL * 1024 * 1024).ConfigureDevice(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);
        string grid = VolumeCommandAuthoring.WriteDensityGrid(
            Path.Combine(AppContext.BaseDirectory, "TestResults", "joint-volumes"),
            $"staged-ownership-{backend}-{textured}",
            8,
            8,
            8,
            static (_, _, _) => 1);
        using (OpenUsdSilkPage staged = Page(
            1,
            Frame(SilkMeshRendererConformance.Identity()),
            Material(MaterialA, 1, [1, 0, 0], grid, textured, false),
            Mesh(MeshA, MaterialA, -1, 1, 1)))
        {
            _ = renderer.ApplyAndRender(staged, color, depth);
        }

        float[] publishedPixels = ReadPixels(color);
        int center = checked(((int)(Size / 2) * (int)Size + (int)(Size / 2)) * 4);
        double[] expected = [1 - Math.Exp(-1), 0, 0];
        AssertColor(
            [publishedPixels[center], publishedPixels[center + 1], publishedPixels[center + 2]],
            expected);
        int publishedAtlasCount = device.CreatedVolumeTextures;
        await Assert.That(publishedAtlasCount).IsEqualTo(textured ? 1 : 0);

        for (int redraw = 0; redraw < 2; redraw++)
        {
            _ = renderer.Render(color, depth);
            float[] redrawnPixels = ReadPixels(color);
            AssertColor(
                [redrawnPixels[center], redrawnPixels[center + 1], redrawnPixels[center + 2]],
                expected);
            float maximumDelta = 0;
            for (int component = 0; component < redrawnPixels.Length; component++)
            {
                maximumDelta = Math.Max(
                    maximumDelta, Math.Abs(redrawnPixels[component] - publishedPixels[component]));
            }
            await Assert.That(maximumDelta).IsLessThanOrEqualTo(1e-6f);
            await Assert.That(device.CreatedVolumeTextures).IsEqualTo(publishedAtlasCount);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task MissingSampledFieldRefusesUpdateAndPreservesPublishedFrame(
        SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice inner = SilkDepthCaptureConformance.CreateDevice(backend);
        using var device = new InjectableVolumeDevice(inner);
        new SilkGpuBufferBudget(16UL * 1024 * 1024).ConfigureDevice(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);
        string grid = VolumeCommandAuthoring.WriteDensityGrid(
            Path.Combine(AppContext.BaseDirectory, "TestResults", "joint-volumes"),
            $"missing-source-{backend}-{Guid.NewGuid():N}",
            8,
            8,
            8,
            static (_, _, _) => 1);
        string missingGrid = grid + ".missing";
        using OpenUsdSilkPage initial = Page(
            1,
            Frame(SilkMeshRendererConformance.Identity()),
            Material(MaterialA, 1, [1, 0, 0], grid, false, false),
            Mesh(MeshA, MaterialA, -1, 1, 1));
        _ = renderer.ApplyAndRender(initial, color, depth);
        float[] publishedPixels = ReadPixels(color);

        using OpenUsdSilkPage missing = Page(
            2,
            Material(MaterialA, 1, [0, 1, 0], missingGrid, true, false));
        await Assert.That(() => renderer.ApplyAndRender(missing, color, depth))
            .Throws<InvalidDataException>()
            .WithMessageContaining(missingGrid);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1UL);
        await Assert.That(ReadPixels(color).AsSpan().SequenceEqual(publishedPixels)).IsTrue();
        await Assert.That(device.CreatedVolumeTextures).IsEqualTo(0);

        using OpenUsdSilkPage corrected = Page(
            2,
            Material(MaterialA, 1, [0, 1, 0], grid, true, false));
        _ = renderer.ApplyAndRender(corrected, color, depth);
        float[] correctedPixels = ReadPixels(color);
        int center = checked(((int)(Size / 2) * (int)Size + (int)(Size / 2)) * 4);
        await Assert.That(correctedPixels[center + 1]).IsGreaterThan(0.1f);
        await Assert.That(correctedPixels.AsSpan().SequenceEqual(publishedPixels)).IsFalse();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task ZeroOpacityFieldsDoNotConsumeActiveFieldSlotsAndRetryAfterRefusal(
        SilkGraphicsBackend backend)
    {
        const string inactiveMaterial = "/World/JointCamera/InactiveMaterial";
        string missingGrid = Path.Combine(
            AppContext.BaseDirectory,
            "TestResults",
            "joint-volumes",
            $"inactive-{Guid.NewGuid():N}.r32");
        using ISilkGraphicsDevice inner = SilkDepthCaptureConformance.CreateDevice(backend);
        using var device = new InjectableVolumeDevice(inner);
        new SilkGpuBufferBudget(16UL * 1024 * 1024).ConfigureDevice(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);
        using OpenUsdSilkPage initial = Page(
            1,
            Frame(SilkMeshRendererConformance.Identity()),
            Material(MaterialA, 1, [1, 0, 0], missingGrid, false, false),
            Mesh(MeshA, MaterialA, -1, 1, 1));
        _ = renderer.ApplyAndRender(initial, color, depth);
        float[] publishedPixels = ReadPixels(color);
        int center = checked(((int)(Size / 2) * (int)Size + (int)(Size / 2)) * 4);
        await Assert.That(publishedPixels[center]).IsGreaterThan(0.1f);

        var firstInactive = new List<byte[]>
        {
            VolumeCommandAuthoring.CreateVolumeMaterialCommand(
                inactiveMaterial, 1, missingGrid, "8,8,8", opacity: 0)
        };
        for (int index = 0; index < 7; index++)
        {
            firstInactive.Add(Mesh(
                $"/World/JointCamera/Inactive{index}", inactiveMaterial, -1, 1, (ulong)(20 + index)));
        }
        using OpenUsdSilkPage belowBoundary = Page(2, [.. firstInactive]);
        _ = renderer.ApplyAndRender(belowBoundary, color, depth);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(2UL);
        await Assert.That(ReadPixels(color).AsSpan().SequenceEqual(publishedPixels)).IsTrue();

        using OpenUsdSilkPage aboveBoundary = Page(
            3,
            Mesh("/World/JointCamera/Inactive7", inactiveMaterial, -1, 1, 27));
        _ = renderer.ApplyAndRender(aboveBoundary, color, depth);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(3UL);
        await Assert.That(ReadPixels(color).AsSpan().SequenceEqual(publishedPixels)).IsTrue();
        await Assert.That(device.CreatedVolumeTextures).IsEqualTo(0);

        using OpenUsdSilkPage tooManyActive = Page(
            4,
            VolumeCommandAuthoring.CreateVolumeMaterialCommand(
                inactiveMaterial, 1, missingGrid, "8,8,8", opacity: 1));
        InvalidDataException exception = (await Assert.That(
            () => renderer.ApplyAndRender(tooManyActive, color, depth))
            .Throws<InvalidDataException>())!;
        await Assert.That(exception.Message).Contains("received 9");
        await Assert.That(renderer.Scene.Revision).IsEqualTo(3UL);
        await Assert.That(ReadPixels(color).AsSpan().SequenceEqual(publishedPixels)).IsTrue();
        _ = renderer.Render(color, depth);
        await Assert.That(ReadPixels(color).AsSpan().SequenceEqual(publishedPixels)).IsTrue();

        using OpenUsdSilkPage retry = Page(
            4,
            VolumeCommandAuthoring.CreateVolumeMaterialCommand(
                inactiveMaterial, 1, missingGrid, "8,8,8", opacity: 0));
        _ = renderer.ApplyAndRender(retry, color, depth);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(4UL);
        await Assert.That(ReadPixels(color).AsSpan().SequenceEqual(publishedPixels)).IsTrue();
        await Assert.That(device.CreatedVolumeTextures).IsEqualTo(0);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeTextureFailure.UniformSurfaceMap)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeTextureFailure.UniformSurfaceMap)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeTextureFailure.UniformSurfaceMap)]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeTextureFailure.ZeroOpacitySurfaceMap)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeTextureFailure.ZeroOpacitySurfaceMap)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeTextureFailure.ZeroOpacitySurfaceMap)]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeTextureFailure.SampledSurfaceMap)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeTextureFailure.SampledSurfaceMap)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeTextureFailure.SampledSurfaceMap)]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeTextureFailure.DuplicateDensity)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeTextureFailure.DuplicateDensity)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeTextureFailure.DuplicateDensity)]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeTextureFailure.CompositeDensity)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeTextureFailure.CompositeDensity)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeTextureFailure.CompositeDensity)]
    public async Task UnsupportedVolumeTexturesRefusePublicationAndAllowCorrectedRetry(
        SilkGraphicsBackend backend,
        VolumeTextureFailure failure)
    {
        using ISilkGraphicsDevice inner = SilkDepthCaptureConformance.CreateDevice(backend);
        using var device = new InjectableVolumeDevice(inner);
        new SilkGpuBufferBudget(16UL * 1024 * 1024).ConfigureDevice(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);
        string grid = VolumeCommandAuthoring.WriteDensityGrid(
            Path.Combine(AppContext.BaseDirectory, "TestResults", "joint-volumes"),
            $"unsupported-texture-{backend}-{failure}-{Guid.NewGuid():N}",
            8,
            8,
            8,
            static (_, _, _) => 1);
        using OpenUsdSilkPage initial = Page(
            1,
            Frame(SilkMeshRendererConformance.Identity()),
            Material(MaterialA, 1, [1, 0, 0], grid, false, false),
            Mesh(MeshA, MaterialA, -1, 1, 1));
        _ = renderer.ApplyAndRender(initial, color, depth);
        float[] publishedPixels = ReadPixels(color);

        bool surfaceMap = failure is VolumeTextureFailure.UniformSurfaceMap or
            VolumeTextureFailure.ZeroOpacitySurfaceMap or VolumeTextureFailure.SampledSurfaceMap;
        var extraTexture = surfaceMap
            ? new VolumeCommandAuthoring.TextureSpec(
                grid + ".png", SilkMaterialParameter.DiffuseColor, "st", 3, SilkTextureChannel.Rgb)
            : new VolumeCommandAuthoring.TextureSpec(
                grid, SilkMaterialParameter.VolumeDensity, "8,8,8", 1, SilkTextureChannel.R,
                failure == VolumeTextureFailure.CompositeDensity
                    ? SilkCompositeOperator.Mix
                    : SilkCompositeOperator.None,
                failure == VolumeTextureFailure.CompositeDensity ? 0.5f : 0);
        bool sampled = failure is not (VolumeTextureFailure.UniformSurfaceMap or
            VolumeTextureFailure.ZeroOpacitySurfaceMap);
        using OpenUsdSilkPage invalid = Page(
            2,
            VolumeCommandAuthoring.CreateVolumeMaterialCommand(
                MaterialA, 1, sampled ? grid : null, sampled ? "8,8,8" : null, [extraTexture],
                opacity: failure == VolumeTextureFailure.ZeroOpacitySurfaceMap ? 0 : null));

        InvalidDataException exception = (await Assert.That(
            () => renderer.ApplyAndRender(invalid, color, depth))
            .Throws<InvalidDataException>())!;
        await Assert.That(exception.Message).Contains(MeshA);
        await Assert.That(exception.Message).Contains(
            surfaceMap ? nameof(SilkMaterialParameter.DiffuseColor) : nameof(SilkMaterialParameter.VolumeDensity));
        if (surfaceMap)
        {
            await Assert.That(exception.Message).Contains(nameof(SilkShaderFeatures.BaseColorMap));
        }
        if (failure == VolumeTextureFailure.CompositeDensity)
        {
            await Assert.That(exception.Message).Contains(nameof(SilkCompositeOperator.Mix));
        }
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1UL);
        await Assert.That(ReadPixels(color).AsSpan().SequenceEqual(publishedPixels)).IsTrue();
        await Assert.That(device.CreatedVolumeTextures).IsEqualTo(0);

        _ = renderer.Render(color, depth);
        await Assert.That(ReadPixels(color).AsSpan().SequenceEqual(publishedPixels)).IsTrue();
        using OpenUsdSilkPage corrected = Page(
            2,
            Material(MaterialA, 1, [0, 1, 0], grid, false, false));
        _ = renderer.ApplyAndRender(corrected, color, depth);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(2UL);
        float[] correctedPixels = ReadPixels(color);
        int center = checked(((int)(Size / 2) * (int)Size + (int)(Size / 2)) * 4);
        await Assert.That(correctedPixels[center + 1]).IsGreaterThan(0.1f);
        await Assert.That(correctedPixels.AsSpan().SequenceEqual(publishedPixels)).IsFalse();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task PublishedAtlasRemainsAvailableWhenSourceFileIsMoved(
        SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice inner = SilkDepthCaptureConformance.CreateDevice(backend);
        using var device = new InjectableVolumeDevice(inner);
        new SilkGpuBufferBudget(16UL * 1024 * 1024).ConfigureDevice(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);
        string grid = VolumeCommandAuthoring.WriteDensityGrid(
            Path.Combine(AppContext.BaseDirectory, "TestResults", "joint-volumes"),
            $"retained-source-{backend}-{Guid.NewGuid():N}",
            8,
            8,
            8,
            static (_, _, _) => 1);
        using OpenUsdSilkPage initial = Page(
            1,
            Frame(SilkMeshRendererConformance.Identity()),
            Material(MaterialA, 1, [1, 0, 0], grid, true, false),
            Mesh(MeshA, MaterialA, -1, 1, 1));
        _ = renderer.ApplyAndRender(initial, color, depth);
        float[] publishedPixels = ReadPixels(color);
        await Assert.That(device.CreatedVolumeTextures).IsEqualTo(1);

        string movedGrid = grid + ".moved";
        File.Move(grid, movedGrid);
        try
        {
            _ = renderer.Render(color, depth);
            await Assert.That(ReadPixels(color).AsSpan().SequenceEqual(publishedPixels)).IsTrue();
            await Assert.That(device.CreatedVolumeTextures).IsEqualTo(1);
        }
        finally
        {
            File.Move(movedGrid, grid);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeCameraFailure.CpuAdmission)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeCameraFailure.CpuAdmission)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeCameraFailure.CpuAdmission)]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeCameraFailure.GpuAdmission)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeCameraFailure.GpuAdmission)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeCameraFailure.GpuAdmission)]
    [Arguments(SilkGraphicsBackend.D3D12, VolumeCameraFailure.AtlasSubmission)]
    [Arguments(SilkGraphicsBackend.Vulkan, VolumeCameraFailure.AtlasSubmission)]
    [Arguments(SilkGraphicsBackend.Metal, VolumeCameraFailure.AtlasSubmission)]
    public async Task RendererAtlasReplacementRefusalPreservesPixelsAndRetriesAfterCorrection(
        SilkGraphicsBackend backend,
        VolumeCameraFailure failure)
    {
        using ISilkGraphicsDevice inner = SilkDepthCaptureConformance.CreateDevice(backend);
        using var device = new InjectableVolumeDevice(inner);
        var gpuBudget = new SilkGpuTextureBudget(64UL * 1024 * 1024);
        gpuBudget.ConfigureDevice(device);
        var cpuBudget = new SilkCpuTextureBudget(64UL * 1024 * 1024);
        cpuBudget.ConfigureDevice(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(
            device,
            new SilkTextureResidencyOptions(),
            cpuBudget);

        string oldGrid = VolumeCommandAuthoring.WriteDensityGrid(
            Path.Combine(AppContext.BaseDirectory, "TestResults", "joint-volumes"),
            $"replacement-old-{backend}-{failure}",
            1,
            1,
            1,
            static (_, _, _) => 1);
        string replacementGrid = VolumeCommandAuthoring.WriteDensityGrid(
            Path.Combine(AppContext.BaseDirectory, "TestResults", "joint-volumes"),
            $"replacement-new-{backend}-{failure}",
            8,
            8,
            8,
            static (_, _, _) => 1);
        using OpenUsdSilkPage initial = Page(
            1,
            Frame(SilkMeshRendererConformance.Identity()),
            Material(MaterialA, 1, [1, 0, 0], oldGrid, true, false, "1,1,1"),
            Mesh(MeshA, MaterialA, -1, 2, 1));
        _ = renderer.ApplyAndRender(initial, color, depth);
        float[] publishedPixels = ReadPixels(color);
        ulong publishedGpuBytes = gpuBudget.Usage.ReservedBytes;
        ulong publishedCpuBytes = cpuBudget.Usage.ReservedBytes;
        int publishedAtlasCount = device.CreatedVolumeTextures;

        using OpenUsdSilkPage replacement = Page(
            2,
            Material(MaterialA, 1, [0, 1, 0], replacementGrid, true, false));
        IDisposable? heldAdmission = null;
        try
        {
            switch (failure)
            {
                case VolumeCameraFailure.CpuAdmission:
                    {
                        ulong available = cpuBudget.MaximumBytes - cpuBudget.Usage.ReservedBytes;
                        heldAdmission = cpuBudget.Reserve(available - 3999);
                        break;
                    }
                case VolumeCameraFailure.GpuAdmission:
                    {
                        ulong available = gpuBudget.MaximumBytes - gpuBudget.Usage.ReservedBytes;
                        heldAdmission = gpuBudget.Reserve(
                            new SilkTextureDescriptor(
                                checked((uint)((available - 3996) / sizeof(float))),
                                1,
                                SilkTextureFormat.R32Float,
                                SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination),
                            1);
                        break;
                    }
                case VolumeCameraFailure.AtlasSubmission:
                    device.RefuseSubmission = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(failure));
            }

            switch (failure)
            {
                case VolumeCameraFailure.CpuAdmission:
                    SilkCpuTextureBudgetExceededException cpuRefusal = (await Assert.That(
                        () => renderer.ApplyAndRender(replacement, color, depth))
                        .Throws<SilkCpuTextureBudgetExceededException>())!;
                    await Assert.That(cpuRefusal.RequestedBytes).IsEqualTo(4000UL);
                    break;
                case VolumeCameraFailure.GpuAdmission:
                    InvalidDataException gpuRefusal = (await Assert.That(
                        () => renderer.ApplyAndRender(replacement, color, depth))
                        .Throws<InvalidDataException>())!;
                    await Assert.That(gpuRefusal.InnerException)
                        .IsTypeOf<SilkGpuTextureBudgetExceededException>();
                    await Assert.That(
                        ((SilkGpuTextureBudgetExceededException)gpuRefusal.InnerException!).RequestedBytes)
                        .IsEqualTo(4000UL);
                    break;
                case VolumeCameraFailure.AtlasSubmission:
                    await Assert.That(() => renderer.ApplyAndRender(replacement, color, depth))
                        .Throws<SilkGpuStagingBudgetExceededException>();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(failure));
            }
            heldAdmission?.Dispose();
            heldAdmission = null;
            await Assert.That(renderer.Scene.Revision).IsEqualTo(1UL);
            await Assert.That(ReadPixels(color).AsSpan().SequenceEqual(publishedPixels)).IsTrue();
            await Assert.That(device.CreatedVolumeTextures)
                .IsEqualTo(publishedAtlasCount + (failure == VolumeCameraFailure.AtlasSubmission ? 1 : 0));
            await Assert.That(gpuBudget.Usage.ReservedBytes).IsEqualTo(publishedGpuBytes);
            await Assert.That(cpuBudget.Usage.ReservedBytes).IsEqualTo(publishedCpuBytes);
        }
        finally
        {
            heldAdmission?.Dispose();
            device.RefuseSubmission = false;
        }

        _ = renderer.ApplyAndRender(replacement, color, depth);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(2UL);
        float[] replacedPixels = ReadPixels(color);
        await Assert.That(replacedPixels.AsSpan().SequenceEqual(publishedPixels)).IsFalse();
        await Assert.That(replacedPixels[checked(((int)(Size / 2) * (int)Size + (int)(Size / 2)) * 4 + 1)])
            .IsGreaterThan(0.1f);
        await Assert.That(device.CreatedVolumeTextures)
            .IsGreaterThan(publishedAtlasCount);
        ulong atlasGrowth = (10UL * 10 * 10 - 3UL * 3 * 3) * sizeof(float);
        ulong spareDepthBytes = (ulong)Size * Size * sizeof(float);
        await Assert.That(gpuBudget.Usage.ReservedBytes)
            .IsEqualTo(publishedGpuBytes + atlasGrowth + spareDepthBytes);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    [Arguments(SilkGraphicsBackend.Metal, false)]
    [Arguments(SilkGraphicsBackend.Metal, true)]
    public Task OpaqueUnequalDensityFieldsPreserveExtinctionTintWeights(
        SilkGraphicsBackend backend, bool textured)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size, Size, SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);
        string grid = textured
            ? VolumeCommandAuthoring.WriteDensityGrid(
                Path.Combine(AppContext.BaseDirectory, "TestResults", "joint-volumes"),
                $"opaque-unequal-density-{backend}", 8, 8, 8, static (_, _, _) => 1)
            : string.Empty;
        using OpenUsdSilkPage page = Page(1,
            Frame(SilkMeshRendererConformance.Identity()),
            Material(MaterialA, 100, [1, 0, 0], grid, textured, false),
            Material(MaterialB, 200, [0, 0, 1], grid, textured, false),
            Mesh(MeshA, MaterialA, -1.5, 1, 1),
            Mesh(MeshB, MaterialB, -1.5, 1, 2));
        _ = renderer.ApplyAndRender(page, color, depth);
        float[] pixels = ReadPixels(color);
        int center = checked(((int)(Size / 2) * (int)Size + (int)(Size / 2)) * 4);
        // Both boxes have local chord 1; opacity=1 leaves optical depths 100:200.
        AssertColor([pixels[center], pixels[center + 1], pixels[center + 2]],
            [1d / 3, 0, 2d / 3]);
        return Task.CompletedTask;
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task ExpensiveJointWorkloadRefusalPreservesFrameAndSceneAndRetriesAfterCorrection(
        SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice inner = SilkDepthCaptureConformance.CreateDevice(backend);
        using var device = new InjectableVolumeDevice(inner);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size, Size, SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);
        using OpenUsdSilkPage initial = Page(1,
            Frame(SilkMeshRendererConformance.Identity()),
            Material(MaterialA, 1, [1, 0, 0], string.Empty, false, false),
            Mesh(MeshA, MaterialA, -1, 2, 1));
        _ = renderer.ApplyAndRender(initial, color, depth);
        float[] publishedPixels = ReadPixels(color);
        int publishedAtlasCount = device.CreatedVolumeTextures;
        string grid = VolumeCommandAuthoring.WriteDensityGrid(
            Path.Combine(AppContext.BaseDirectory, "TestResults", "joint-volumes"),
            $"expensive-camera-workload-{backend}", 512, 1, 1, static (_, _, _) => 1);
        byte[][] additions = Enumerable.Range(1, 7).Select(index =>
            Mesh($"{MeshA}_{index}", MaterialA, -1, 2, checked((ulong)index + 1))).ToArray();
        using OpenUsdSilkPage expensive = Page(2,
            [Material(MaterialA, 1, [0, 1, 0], grid, true, false, "512,1,1"), .. additions]);

        await Assert.That(() => renderer.ApplyAndRender(expensive, color, depth))
            .Throws<InvalidDataException>().WithMessageContaining("1966080 evaluations per pixel");
        await Assert.That(renderer.Scene.Meshes.Count).IsEqualTo(1);
        await Assert.That(device.CreatedVolumeTextures).IsEqualTo(publishedAtlasCount);
        await Assert.That(ReadPixels(color).AsSpan().SequenceEqual(publishedPixels)).IsTrue();
        _ = renderer.Render(color, depth);
        await Assert.That(ReadPixels(color).AsSpan().SequenceEqual(publishedPixels)).IsTrue();

        using OpenUsdSilkPage corrected = Page(2,
            Material(MaterialA, 1, [0, 1, 0], string.Empty, false, false));
        _ = renderer.ApplyAndRender(corrected, color, depth);
        float[] replacedPixels = ReadPixels(color);
        await Assert.That(replacedPixels.AsSpan().SequenceEqual(publishedPixels)).IsFalse();
        await Assert.That(replacedPixels[checked(((int)(Size / 2) * (int)Size + (int)(Size / 2)) * 4 + 1)])
            .IsGreaterThan(0.1f);
        await Assert.That(renderer.Scene.Meshes.Count).IsEqualTo(1);
    }

    private static double[] RenderFieldsAtClip(
            SilkGraphicsBackend backend,
            string grid,
            bool useFarPlane,
            bool mixedTextured,
            bool hideVolumeTextureSupport,
            bool includeVolumes = true)
    {
        using ISilkGraphicsDevice device = hideVolumeTextureSupport
            ? new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend))
            : SilkDepthCaptureConformance.CreateDevice(backend);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);
        double far = useFarPlane ? 0.7 : 4;
        byte[] frame = Frame(SilkMeshRendererConformance.Identity(), far);
        var commands = new List<byte[]> { frame };
        if (includeVolumes)
        {
            commands.Add(Material(MaterialA, 0.5f, [1, 0, 0], grid, false, false));
            commands.Add(Mesh(MeshA, MaterialA, -1, 2, 1));
            if (mixedTextured)
            {
                commands.Add(Material(MaterialB, 1, [0, 0, 1], grid, true, false));
                commands.Add(Mesh(MeshB, MaterialB, -1, 2, 2));
            }
        }
        if (!useFarPlane)
        {
            commands.Add(VolumeCommandAuthoring.CreateDepthOccluderMeshCommand(
                "/World/JointCamera/Opaque",
                -0.8f,
                id: 90));
        }
        SilkMeshRendererConformance.Apply(renderer, 1, [.. commands]);
        _ = renderer.Render(color, depth, SilkMeshRenderOptions.Default);
        float[] pixels = ReadPixels(color);
        int offset = checked(((int)(Size / 2) * (int)Size + (int)(Size / 2)) * 4);
        return [pixels[offset], pixels[offset + 1], pixels[offset + 2]];
    }

    private static double[] ExpectedClippedFieldColor(
            double distance,
            double[] background,
            bool mixedTextured)
    {
        const double uniformDensity = 0.5;
        const double texturedDensity = 1;
        // Both fields have z-scale 2; extinction is per field-local unit.
        double uniformDepth = uniformDensity * distance / 2;
        double texturedDepth = mixedTextured ? texturedDensity * distance / 2 : 0;
        double totalDepth = uniformDepth + texturedDepth;
        double alpha = 1 - Math.Exp(-totalDepth);
        double[] volumeTint =
        [
            uniformDepth / totalDepth,
                0,
                texturedDepth / totalDepth
        ];
        return
        [
            alpha * volumeTint[0] + (1 - alpha) * background[0],
                alpha * volumeTint[1] + (1 - alpha) * background[1],
                alpha * volumeTint[2] + (1 - alpha) * background[2]
        ];
    }

    private static float[] ReadPixels(ISilkGraphicsTexture color)
    {
        var pixels = new float[checked((int)(Size * Size * 4))];
        color.ReadbackForTesting(pixels);
        return pixels;
    }

    private static ISilkGraphicsTexture GetPublishedVolumeDepthSnapshot(SilkMeshRenderer renderer)
    {
        var field = typeof(SilkMeshRenderer).GetField(
            "_volumeDepthSnapshot",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The volume-depth observation seam changed.");
        return field.GetValue(renderer) as ISilkGraphicsTexture
            ?? throw new InvalidOperationException("The published volume-depth snapshot is missing.");
    }

    private static OpenUsdSilkPage Page(ulong revision, params byte[][] commands)
    {
        int length = commands.Sum(static command => command.Length);
        var pageBytes = new byte[length];
        int offset = 0;
        foreach (byte[] command in commands)
        {
            command.CopyTo(pageBytes, offset);
            offset += command.Length;
        }
        return new OpenUsdSilkPage(
            SilkCommandParser.PageAbiVersion,
            revision,
            pageBytes,
            checked((uint)commands.Length));
    }

    public enum VolumeCameraFailure
    {
        CpuAdmission,
        GpuAdmission,
        AtlasSubmission
    }

    public enum LowDensitySignal
    {
        SubnormalScale,
        StepUnderflow,
        OpacityProduct,
        SubnormalSample,
    }

    public enum VolumeFrameFailure
    {
        OutputAllocation,
        FrameSubmission,
        OutputPublication,
    }

    public enum VolumeCleanupFailure
    {
        None,
        DepthSnapshot,
        VolumeResources,
        Both,
    }

    public enum VolumeTextureFailure
    {
        UniformSurfaceMap,
        ZeroOpacitySurfaceMap,
        SampledSurfaceMap,
        DuplicateDensity,
        CompositeDensity
    }

    private sealed class InjectableVolumeDevice(ISilkGraphicsDevice inner)
            : SilkGpuPublicationDevice(inner), ISilkVolumeTextureGraphicsDevice
    {
        internal int CreatedVolumeTextures { get; private set; }

        public ISilkGraphicsTexture CreateTexture3D(
            uint width,
            uint height,
            uint depth,
            SilkTextureFormat format)
        {
            ISilkGraphicsTexture texture =
                ((ISilkVolumeTextureGraphicsDevice)Inner).CreateTexture3D(
                    width,
                    height,
                    depth,
                    format);
            CreatedVolumeTextures++;
            return texture;
        }
    }
    private static double[] RenderCenter(
        SilkGraphicsBackend backend,
        string grid,
        bool textured,
        bool missingGrid,
        bool hideVolumeTextureSupport,
        bool reverseInsertion,
        bool reverseCamera)
    {
        using ISilkGraphicsDevice device = hideVolumeTextureSupport
            ? new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend))
            : SilkDepthCaptureConformance.CreateDevice(backend);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba32Float,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);

        byte[] materialA = Material(MaterialA, 0.5f, [1, 0, 0], grid, textured, missingGrid);
        byte[] materialB = Material(MaterialB, 1.25f, [0, 0, 1], grid, textured, missingGrid);
        byte[] materialC = Material(MaterialC, 0.75f, [0, 1, 0], grid, textured, missingGrid);
        byte[] meshA = Mesh(MeshA, MaterialA, -1, 2, id: 1);
        byte[] meshB = Mesh(MeshB, MaterialB, -1.5, 2, id: 2);
        byte[] meshC = Mesh(MeshC, MaterialC, -1.375, 0.75, id: 3);
        double[] view = SilkMeshRendererConformance.Identity();
        if (reverseCamera)
        {
            view[10] = -1;
            view[14] = -3;
        }

        byte[] frame = Frame(view);
        byte[][] commands = reverseInsertion
            ? [frame, materialC, materialB, materialA, meshC, meshB, meshA]
            : [frame, materialA, materialB, materialC, meshA, meshB, meshC];
        SilkMeshRendererConformance.Apply(renderer, 1, commands);
        _ = renderer.Render(color, depth, SilkMeshRenderOptions.Default);

        var pixels = new float[checked((int)(Size * Size * 4))];
        color.ReadbackForTesting(pixels);
        int offset = checked(((int)(Size / 2) * (int)Size + (int)(Size / 2)) * 4);
        return [pixels[offset], pixels[offset + 1], pixels[offset + 2]];
    }

    private static byte[] Material(
        string path,
        float density,
        float[] tint,
        string grid,
        bool textured,
        bool missingGrid,
        string dimensions = "8,8,8",
        float opacity = 1)
    {
        string? textureAsset = textured ? grid : missingGrid ? $"{grid}.missing" : null;
        byte[] command = VolumeCommandAuthoring.CreateVolumeMaterialCommand(
            path,
            density,
            textureAsset,
            textureAsset is null ? null : dimensions,
            opacity: opacity);
        int colorOffset = 32 + Encoding.UTF8.GetByteCount(path) + 8;
        for (int component = 0; component < 3; component++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                command.AsSpan(colorOffset + component * sizeof(float)),
                tint[component]);
        }
        return command;
    }

    private static byte[] Mesh(string path, string material, double z, double scale, ulong id)
    {
        double[] transform = SilkMeshRendererConformance.Identity();
        transform[10] = scale;
        transform[14] = z;
        return VolumeCommandAuthoring.WithTransform(
            VolumeCommandAuthoring.CreateVolumeCameraProxyMeshCommand(
                path,
                material,
                id: id),
            transform);
    }

    private static byte[] Frame(double[] view, double far = 4)
    {
        byte[] frame = SilkMeshRendererConformance.CreateFrameCommand(Size, Size, view);
        double near = 0.1;
        double[] projection =
        [
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, -2 / (far - near), 0,
            0, 0, -(far + near) / (far - near), 1,
        ];
        for (int index = 0; index < projection.Length; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                frame.AsSpan(144 + index * sizeof(double)),
                projection[index]);
        }
        return frame;
    }

    private static (double[] Forward, double[] Reverse) ExpectedNestedColors(
        double[] tintA,
        double[] tintB,
        double[] tintC)
    {
        double[] forward = [0, 0, 0];
        double[] reverse = [0, 0, 0];
        double forwardTransmission = 1;
        double reverseTransmission = 1;

        // A/B have z-scale 2; C has z-scale .75.
        CompositeSegment(forward, ref forwardTransmission, [0.1], [tintA]);
        CompositeSegment(forward, ref forwardTransmission, [0.125, 0.3125], [tintA, tintB]);
        CompositeSegment(forward, ref forwardTransmission,
            [0.1875, 0.46875, 0.75], [tintA, tintB, tintC]);
        CompositeSegment(forward, ref forwardTransmission, [0.0625, 0.15625], [tintA, tintB]);
        CompositeSegment(forward, ref forwardTransmission, [0.3125], [tintB]);

        CompositeSegment(reverse, ref reverseTransmission, [0.3125], [tintB]);
        CompositeSegment(reverse, ref reverseTransmission, [0.0625, 0.15625], [tintA, tintB]);
        CompositeSegment(reverse, ref reverseTransmission, [0.1875, 0.46875, 0.75],
            [tintA, tintB, tintC]);
        CompositeSegment(reverse, ref reverseTransmission, [0.125, 0.3125], [tintA, tintB]);
        CompositeSegment(reverse, ref reverseTransmission, [0.125], [tintA]);

        return (forward, reverse);
    }

    private static void CompositeSegment(
        double[] accumulated,
        ref double transmittance,
        double[] opticalDepths,
        double[][] tints)
    {
        double segmentOpticalDepth = opticalDepths.Sum();
        if (segmentOpticalDepth <= 0)
        {
            return;
        }

        var source = new double[3];
        for (int field = 0; field < opticalDepths.Length; field++)
        {
            for (int channel = 0; channel < source.Length; channel++)
            {
                source[channel] += opticalDepths[field] * tints[field][channel];
            }
        }
        double alpha = 1 - Math.Exp(-segmentOpticalDepth);
        for (int channel = 0; channel < source.Length; channel++)
        {
            accumulated[channel] += transmittance * alpha *
                source[channel] / segmentOpticalDepth;
        }
        transmittance *= Math.Exp(-segmentOpticalDepth);
    }

    private static void AssertColor(double[] actual, double[] expected)
    {
        for (int channel = 0; channel < 3; channel++)
        {
            double absoluteError = Math.Abs(actual[channel] - expected[channel]);
            if (!double.IsFinite(actual[channel]) ||
                !double.IsFinite(expected[channel]) ||
                absoluteError > 0.01)
            {
                throw new InvalidOperationException(
                    $"Joint volume channel {channel} was {actual[channel]:R}; " +
                    $"the independent piecewise Beer-Lambert reference is {expected[channel]:R}.");
            }
        }
    }
}
