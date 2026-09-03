// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.Vulkan;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// Executable Vulkan evidence that the colour-managed display transform runs on the
/// GPU and agrees with the CPU export path it is derived from.
/// </summary>
[NotInParallel]
public sealed class VulkanDisplayTransformTests
{
    private const uint Width = 32;
    private const uint Height = 24;

    /// <summary>
    /// The bound the GPU lattice is allowed to differ from the CPU processor by, in
    /// 8-bit display code values. Kept identical to the D3D12 gate so a backend that
    /// silently interpolated differently would fail here.
    /// </summary>
    private const int MaximumCodeValueDifference = 2;

    [Test]
    public async Task SwiftShaderAppliesConfiguredDisplayTransformToClearedFrame()
    {
        if (!IsSupportedPlatform())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        string configPath = RequireTransformableConfigPath();
        var clearColor = new SilkColor(0.25f, 0.5f, 0.75f, 1);
        const float exposure = -1f;

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        RequireSwiftShader(device);

        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture display = CreateDisplayTarget(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Width, Height));

        _ = renderer.Render(
            display,
            depth,
            new SilkMeshRenderOptions(clearColor, 1)
            {
                DisplayTransform = new RenderDisplayTransform(
                    configPath,
                    "linear",
                    "TestDisplay",
                    "TestView"),
                Exposure = exposure,
            });

        byte[] rendered = ReadPixels(display);
        byte[] expected = ApplyCpuProcessor(configPath, clearColor, exposure);

        await Assert.That(renderer.DisplayTransformDiagnostics.Status)
            .IsEqualTo(SilkDisplayTransformStatus.Applied);
        await Assert.That(renderer.DisplayTransformDiagnostic).IsNull();
        for (int pixel = 0; pixel < rendered.Length; pixel += 4)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                await Assert.That(
                        Math.Abs(rendered[pixel + channel] - expected[channel]))
                    .IsLessThanOrEqualTo(MaximumCodeValueDifference);
            }
            await Assert.That(rendered[pixel + 3]).IsEqualTo((byte)255);
        }
    }

    [Test]
    public async Task SwiftShaderRestoresUntransformedOutputWhenTransformIsCleared()
    {
        if (!IsSupportedPlatform())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        string configPath = RequireTransformableConfigPath();
        var clearColor = new SilkColor(0.25f, 0.5f, 0.75f, 1);

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        RequireSwiftShader(device);

        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture display = CreateDisplayTarget(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Width, Height));

        _ = renderer.Render(display, depth, new SilkMeshRenderOptions(clearColor, 1));
        byte[] baseline = ReadPixels(display);

        _ = renderer.Render(
            display,
            depth,
            new SilkMeshRenderOptions(clearColor, 1)
            {
                DisplayTransform = new RenderDisplayTransform(
                    configPath,
                    "linear",
                    "TestDisplay",
                    "TestView"),
            });
        byte[] transformed = ReadPixels(display);

        _ = renderer.Render(display, depth, new SilkMeshRenderOptions(clearColor, 1));
        byte[] restored = ReadPixels(display);

        for (int channel = 0; channel < 3; channel++)
        {
            await Assert.That(Math.Abs(transformed[channel] - baseline[channel]))
                .IsGreaterThan(16);
        }
        await Assert.That(restored.SequenceEqual(baseline)).IsTrue();
        await Assert.That(renderer.DisplayTransformDiagnostics.Status)
            .IsEqualTo(SilkDisplayTransformStatus.Inactive);
    }

    [Test]
    public async Task SwiftShaderReportsMissingConfigInsteadOfSilentIdentity()
    {
        if (!IsSupportedPlatform())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        var clearColor = new SilkColor(0.25f, 0.5f, 0.75f, 1);
        string missingConfig = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            $"openusd-missing-{Guid.NewGuid():N}.ocio");

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        RequireSwiftShader(device);

        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture display = CreateDisplayTarget(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Width, Height));

        _ = renderer.Render(display, depth, new SilkMeshRenderOptions(clearColor, 1));
        byte[] baseline = ReadPixels(display);

        _ = renderer.Render(
            display,
            depth,
            new SilkMeshRenderOptions(clearColor, 1)
            {
                DisplayTransform = new RenderDisplayTransform(
                    missingConfig,
                    "linear",
                    "TestDisplay",
                    "TestView"),
            });
        byte[] fallback = ReadPixels(display);

        await Assert.That(renderer.DisplayTransformDiagnostics.Status)
            .IsEqualTo(SilkDisplayTransformStatus.ConfigUnavailable);
        await Assert.That(renderer.DisplayTransformDiagnostic).IsNotNull();
        await Assert.That(fallback.SequenceEqual(baseline)).IsTrue();
    }

    [Test]
    public async Task SwiftShaderDisplayTransformPreservesVerticalOrientation()
    {
        if (!IsSupportedPlatform())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        string configPath = RequireTransformableConfigPath();

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        RequireSwiftShader(device);

        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture display = CreateDisplayTarget(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Width, Height));
        DisplayTransformConformance.ApplyVerticallyAsymmetricScene(renderer);

        var options = new SilkMeshRenderOptions(new SilkColor(0, 0, 0, 1), 1);
        _ = renderer.Render(display, depth, options);
        byte[] untransformed = ReadPixels(display);
        (double Top, double Bottom) baseline =
            DisplayTransformConformance.MeasureVerticalBias(untransformed);

        _ = renderer.Render(
            display,
            depth,
            options with
            {
                // The identity view maps the working space to itself, so this pass is a
                // pure blit. Vulkan's framebuffer origin opposes the fullscreen
                // triangle's clip-space Y, so before the sampled coordinate was flipped
                // this wrote the source's top row into the target's bottom row: the top
                // and bottom measurements swapped and this assertion failed.
                DisplayTransform = new RenderDisplayTransform(
                    configPath,
                    "linear",
                    "TestDisplay",
                    "IdentityView"),
            });
        byte[] transformed = ReadPixels(display);
        (double Top, double Bottom) measured =
            DisplayTransformConformance.MeasureVerticalBias(transformed);

        await Assert.That(renderer.DisplayTransformDiagnostics.Status)
            .IsEqualTo(SilkDisplayTransformStatus.Applied);

        // The premise of this gate: Vulkan's framebuffer origin opposes the fullscreen
        // triangle's clip-space Y. If that ever stopped being true the flip would be
        // wrong in the other direction, so it is asserted rather than assumed.
        await Assert.That(device.ClipSpaceYPointsDown).IsTrue();
        await Assert.That(baseline.Top).IsGreaterThan(baseline.Bottom + 64);
        await Assert.That(measured.Top).IsGreaterThan(measured.Bottom + 64);
        await Assert.That(measured.Top).IsEqualTo(baseline.Top).Within(3);
        await Assert.That(measured.Bottom).IsEqualTo(baseline.Bottom).Within(3);
    }

    [Test]
    public async Task SwiftShaderLookOverrideMatchesTheCpuExportPath()
    {
        if (!IsSupportedPlatform())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        string configPath = RequireLookConfigPath();
        var clearColor = new SilkColor(0.5f, 0.5f, 0.5f, 1);

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        RequireSwiftShader(device);

        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture display = CreateDisplayTarget(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Width, Height));

        _ = renderer.Render(
            display,
            depth,
            new SilkMeshRenderOptions(clearColor, 1)
            {
                DisplayTransform = new RenderDisplayTransform(
                    configPath,
                    "linear",
                    "LookDisplay",
                    "ViewWithLook",
                    "OverrideLook"),
            });
        byte[] rendered = ReadPixels(display);

        await Assert.That(renderer.DisplayTransformDiagnostics.Status)
            .IsEqualTo(SilkDisplayTransformStatus.Applied);

        // 0.5 linear -> look_space 0.125 -> ^2 -> reference -> display = 0.03125.
        for (int channel = 0; channel < 3; channel++)
        {
            await Assert.That(
                    Math.Abs(rendered[channel] - (int)Math.Round(0.03125 * 255)))
                .IsLessThanOrEqualTo(MaximumCodeValueDifference);
        }
    }

    [Test]
    public async Task SwiftShaderReuploadsTheLatticeWhenTheConfigChangesBetweenRenders()
    {
        if (!IsSupportedPlatform())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        string sourceConfig = RequireTransformableConfigPath();
        var clearColor = new SilkColor(0.25f, 0.5f, 0.75f, 1);
        string root = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            $"swiftshader-config-edit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string configPath = Path.Combine(root, "config.ocio");
        try
        {
            File.Copy(sourceConfig, configPath);

            using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
            RequireSwiftShader(device);

            using var renderer = new SilkMeshRenderer(device);
            using ISilkGraphicsTexture display = CreateDisplayTarget(device);
            using ISilkGraphicsTexture depth = device.CreateTexture2D(
                SilkTextureDescriptor.SampledDepthTarget(Width, Height));

            var options = new SilkMeshRenderOptions(clearColor, 1)
            {
                DisplayTransform = new RenderDisplayTransform(
                    configPath,
                    "linear",
                    "TestDisplay",
                    "TestView",
                    latticeSize: 32),
            };

            _ = renderer.Render(display, depth, options);
            byte[] before = ReadPixels(display);

            File.WriteAllText(
                configPath,
                File.ReadAllText(configPath)
                    .Replace("gamma: 2.4", "gamma: 1.6", StringComparison.Ordinal));
            await Task.Delay(
                SilkOpenColorIoConfigIdentityProvider.DefaultRevalidationInterval +
                TimeSpan.FromMilliseconds(150));

            _ = renderer.Render(display, depth, options);
            byte[] after = ReadPixels(display);
            SilkDisplayTransformDiagnostics diagnostics =
                renderer.DisplayTransformDiagnostics;

            await Assert.That(diagnostics.Status)
                .IsEqualTo(SilkDisplayTransformStatus.Applied);
            await Assert.That(diagnostics.LatticeUploads).IsEqualTo(2UL);
            await Assert.That(after.SequenceEqual(before)).IsFalse();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task SwiftShaderReleasesDisplayTransformResourcesAcrossEveryRebuild()
    {
        if (!IsSupportedPlatform())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        string configPath = RequireTransformableConfigPath();
        var clearColor = new SilkColor(0.25f, 0.5f, 0.75f, 1);

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        RequireSwiftShader(device);

        using ISilkGraphicsTexture small = CreateDisplayTarget(device);
        using ISilkGraphicsTexture smallDepth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Width, Height));
        using ISilkGraphicsTexture large = device.CreateTexture2D(
            new SilkTextureDescriptor(
                Width * 2,
                Height * 2,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget |
                    SilkTextureUsage.Sampled |
                    SilkTextureUsage.CopySource |
                    SilkTextureUsage.CopyDestination));
        using ISilkGraphicsTexture largeDepth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Width * 2, Height * 2));

        var renderer = new SilkMeshRenderer(device);
        try
        {
            var options = new SilkMeshRenderOptions(clearColor, 1)
            {
                DisplayTransform = new RenderDisplayTransform(
                    configPath,
                    "linear",
                    "TestDisplay",
                    "TestView"),
            };
            var otherView = options with
            {
                DisplayTransform = new RenderDisplayTransform(
                    configPath,
                    "linear",
                    "TestDisplay",
                    "IdentityView"),
            };

            // A resize replaces the intermediate and therefore the binding; a different
            // view replaces the lattice; a device-generation change replaces the
            // pipeline, the binding, and the descriptor-set layout each binding owns.
            _ = renderer.Render(small, smallDepth, options);
            _ = renderer.Render(large, largeDepth, options);
            _ = renderer.Render(small, smallDepth, otherView);
            _ = renderer.Render(small, smallDepth, options);
            device.InvalidateSelectionOutlineDeviceGenerationForTesting();
            _ = renderer.Render(small, smallDepth, options);

            VulkanDisplayTransformNativeStatistics rebuilt =
                device.DisplayTransformNativeStatisticsForTesting;
            SilkDisplayTransformDiagnostics diagnostics =
                renderer.DisplayTransformDiagnostics;

            await Assert.That(diagnostics.Status)
                .IsEqualTo(SilkDisplayTransformStatus.Applied);

            // Every rebuild released its predecessor: exactly one pipeline and one
            // binding are alive no matter how many were created, and a descriptor-set
            // layout is owned by, and dies with, its binding.
            await Assert.That(rebuilt.ActivePipelines).IsEqualTo(1L);
            await Assert.That(rebuilt.ActiveBindings).IsEqualTo(1L);
            await Assert.That(rebuilt.BindingCreations).IsGreaterThan(1L);
            await Assert.That(rebuilt.PipelineCreations).IsGreaterThanOrEqualTo(2L);

            // Counted at the native call, not on the managed wrapper: every binding
            // created a VkDescriptorSetLayout and every superseded binding destroyed
            // exactly one, so at most the live binding's layout is outstanding.
            await Assert.That(rebuilt.SetLayoutCreations)
                .IsEqualTo(rebuilt.BindingCreations);
            await Assert.That(rebuilt.LiveSetLayouts).IsEqualTo(1L);
        }
        finally
        {
            renderer.Dispose();
        }

        VulkanDisplayTransformNativeStatistics disposed =
            device.DisplayTransformNativeStatisticsForTesting;
        await Assert.That(disposed.ActivePipelines).IsEqualTo(0L);
        await Assert.That(disposed.ActiveBindings).IsEqualTo(0L);

        // The decisive leak assertion: vkDestroyDescriptorSetLayout ran once for every
        // vkCreateDescriptorSetLayout. Before the layout was destroyed at all, this was
        // equal to the number of bindings ever created while every wrapper count above
        // still read zero.
        await Assert.That(disposed.SetLayoutDestructions)
            .IsEqualTo(disposed.SetLayoutCreations);
        await Assert.That(disposed.LiveSetLayouts).IsEqualTo(0L);
    }

    [Test]
    public async Task SwiftShaderDestroysEveryDescriptorSetLayoutItCreatesForABinding()
    {
        if (!IsSupportedPlatform())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        RequireSwiftShader(device);

        using ISilkGraphicsTexture sceneColor = device.CreateTexture2D(
            SilkTextureDescriptor.HdrColorTarget(Width, Height));
        using ISilkGraphicsTexture lattice = device.CreateTexture2D(
            SilkTextureDescriptor.SampledRgba8(64, 8));
        using ISilkGraphicsSampler sampler = device.CreateSampler(
            SilkSamplerDescriptor.LinearClamp);
        using ISilkGraphicsBuffer parameters = device.CreateBuffer(
            SilkDisplayTransformUniformWriter.ByteSize,
            SilkBufferUsage.Uniform | SilkBufferUsage.Upload);

        long before = device.DisplayTransformNativeStatisticsForTesting.LiveSetLayouts;
        await Assert.That(before).IsEqualTo(0L);

        var descriptor = new SilkDisplayTransformBindingDescriptor(
            sceneColor,
            lattice,
            sampler,
            parameters);

        ISilkDisplayTransformBinding binding =
            device.CreateDisplayTransformBinding(descriptor);
        VulkanDisplayTransformNativeStatistics live =
            device.DisplayTransformNativeStatisticsForTesting;
        await Assert.That(live.SetLayoutCreations).IsEqualTo(1L);
        await Assert.That(live.LiveSetLayouts).IsEqualTo(1L);

        binding.Dispose();

        // The layout is a native handle the wrapper owns. Before it was destroyed at
        // all, ActiveBindings still returned to zero here and only this counter moved,
        // which is precisely why the wrapper counts could not see the leak.
        VulkanDisplayTransformNativeStatistics released =
            device.DisplayTransformNativeStatisticsForTesting;
        await Assert.That(released.SetLayoutDestructions).IsEqualTo(1L);
        await Assert.That(released.LiveSetLayouts).IsEqualTo(0L);
        await Assert.That(released.ActiveBindings).IsEqualTo(0L);

        // Disposing twice must not destroy the handle twice.
        binding.Dispose();
        await Assert.That(
            device.DisplayTransformNativeStatisticsForTesting.SetLayoutDestructions)
            .IsEqualTo(1L);

        // A rejected create never reaches the layout, so it must not move the counter
        // in either direction.
        using ISilkGraphicsBuffer disposedParameters = device.CreateBuffer(
            SilkDisplayTransformUniformWriter.ByteSize,
            SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
        disposedParameters.Dispose();
        await Assert.That(() => device.CreateDisplayTransformBinding(
            descriptor with { Parameters = disposedParameters }))
            .Throws<ObjectDisposedException>();
        VulkanDisplayTransformNativeStatistics rejected =
            device.DisplayTransformNativeStatisticsForTesting;
        await Assert.That(rejected.SetLayoutCreations).IsEqualTo(1L);
        await Assert.That(rejected.LiveSetLayouts).IsEqualTo(0L);
    }

    [Test]
    public async Task SwiftShaderFailsRatherThanSkippingWhenTheConfigIsMalformed()
    {
        if (!IsSupportedPlatform())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        // Proves the availability probe is not a blanket "OpenColorIO had a problem"
        // skip. The native runtime is required to load; a config it then refuses is a
        // failure this test asserts on, and a transform the config does not contain is
        // another. If either were routed through Skip.Test, this test would report
        // success while asserting nothing.
        string configPath = RequireTransformableConfigPath();

        string malformed = Path.Combine(
            AppContext.BaseDirectory,
            "vulkan-malformed-display-transform.ocio");
        await File.WriteAllTextAsync(malformed, "ocio_profile_version: 2\nthis: [is not");

        var malformedTransform = new RenderDisplayTransform(
            malformed,
            "linear",
            "TestDisplay",
            "TestView");
        SilkDisplayTransformException malformedFailure =
            Assert.Throws<SilkDisplayTransformException>(
                () => SilkOpenColorIoLatticeProvider.Shared.Create(malformedTransform));
        await Assert.That(IsNativeRuntimeAbsence(malformedFailure)).IsFalse();
        await Assert.That(malformedFailure.Status).IsEqualTo(
            SilkDisplayTransformStatus.TransformUnsupported);
        await Assert.That(malformedFailure.Message).IsNotEmpty();

        SilkDisplayTransformException missingView =
            Assert.Throws<SilkDisplayTransformException>(
                () => SilkOpenColorIoLatticeProvider.Shared.Create(
                    new RenderDisplayTransform(
                        configPath,
                        "linear",
                        "TestDisplay",
                        "NoSuchViewExistsHere")));
        await Assert.That(IsNativeRuntimeAbsence(missingView)).IsFalse();
        await Assert.That(missingView.Status).IsEqualTo(
            SilkDisplayTransformStatus.TransformUnsupported);

        File.Delete(malformed);
    }

    private static bool IsSupportedPlatform() =>
        OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    private static void RequireSwiftShader(VulkanSilkGraphicsDevice device)
    {
        if (IsSwiftShader(device))
        {
            return;
        }

        // Named rather than silently returning: a test that quietly passes on a
        // hardware driver proves nothing about the software reference this gate exists
        // to hold, and a skip says so in the run summary.
        Skip.Test(
            "This gate requires the SwiftShader software Vulkan device, but the " +
            $"available device is '{device.Capabilities.DeviceName}'.");
        throw new InvalidOperationException("Skip.Test returned unexpectedly.");
    }

    private static bool IsSwiftShader(VulkanSilkGraphicsDevice device) =>
        device.Capabilities.IsSoftware &&
        device.Capabilities.DeviceName.Contains(
            "SwiftShader",
            StringComparison.OrdinalIgnoreCase);

    private static ISilkGraphicsTexture CreateDisplayTarget(
        VulkanSilkGraphicsDevice device) =>
        device.CreateTexture2D(new SilkTextureDescriptor(
            Width,
            Height,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget |
                SilkTextureUsage.Sampled |
                SilkTextureUsage.CopySource |
                SilkTextureUsage.CopyDestination));

    private static byte[] ReadPixels(ISilkGraphicsTexture texture)
    {
        byte[] pixels = new byte[checked(Width * Height * 4)];
        texture.ReadbackForTesting(pixels);
        return pixels;
    }

    private static string RequireTransformableConfigPath()
    {
        string root = FindRepositoryRoot() ??
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string path = Path.Combine(root, "test-assets", "ocio-test-config.ocio");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "The checked-in OpenColorIO test config is missing. That is a repository " +
                "failure, not a host capability gap, so it is reported rather than skipped.",
                path);
        }

        PrependNativeSearchPath(root);
        RequireOpenColorIoNativeRuntime(
            new RenderDisplayTransform(path, "linear", "TestDisplay", "TestView"));
        return path;
    }

    private static string RequireLookConfigPath()
    {
        string root = FindRepositoryRoot() ??
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string path = Path.Combine(root, "test-assets", "ocio-look-override-config.ocio");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "The checked-in OpenColorIO look config is missing. That is a repository " +
                "failure, not a host capability gap, so it is reported rather than skipped.",
                path);
        }

        PrependNativeSearchPath(root);
        RequireOpenColorIoNativeRuntime(
            new RenderDisplayTransform(
                path,
                "linear",
                "LookDisplay",
                "ViewWithLook",
                "OverrideLook"));
        return path;
    }

    /// <summary>
    /// Skips only when the OpenColorIO native library itself cannot be loaded or an
    /// entry point is missing.
    /// </summary>
    /// <remarks>
    /// Everything else -- a malformed config, a display or view the config does not
    /// contain, a processor that will not build, a lattice that will not bake -- is a
    /// real defect in code this repository owns, and a skip there is indistinguishable
    /// from a pass. Only proven absence of the native runtime is a host capability gap,
    /// and that absence is visible as the inner exception the lattice provider preserves.
    /// </remarks>
    private static void RequireOpenColorIoNativeRuntime(RenderDisplayTransform probe)
    {
        try
        {
            _ = SilkOpenColorIoLatticeProvider.Shared.Create(probe);
        }
        catch (SilkDisplayTransformException exception) when (
            IsNativeRuntimeAbsence(exception))
        {
            Skip.Test(
                "The OpenColorIO native runtime could not be loaded in this host: " +
                exception.Message);
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
    }

    private static bool IsNativeRuntimeAbsence(Exception exception)
    {
        for (Exception? current = exception.InnerException;
            current is not null;
            current = current.InnerException)
        {
            if (current is DllNotFoundException or EntryPointNotFoundException or
                BadImageFormatException)
            {
                return true;
            }
        }

        return false;
    }

    private static void PrependNativeSearchPath(string root)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string[] directories =
        [
            Path.Combine(root, "native", "install", "shim", "win-x64", "bin"),
            Path.Combine(root, "native", "install", "win-x64", "bin"),
            Path.Combine(root, "native", "install", "win-x64", "lib"),
        ];
        string prefix = string.Join(
            Path.PathSeparator,
            directories.Where(Directory.Exists).Select(Path.GetFullPath));
        if (prefix.Length == 0)
        {
            return;
        }
        string current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        Environment.SetEnvironmentVariable(
            "PATH",
            prefix + Path.PathSeparator + current);
    }

    private static string? FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        return null;
    }

    private static byte[] ApplyCpuProcessor(
        string configPath,
        SilkColor color,
        float exposure)
    {
        var transform = new SilkOpenColorIoDisplayTransform(
            configPath,
            "linear",
            "TestDisplay",
            "TestView");
        using SilkOpenColorIoProcessor processor = transform.CreateProcessor();
        byte[] source = new byte[8];
        Span<Half> channels = MemoryMarshal.Cast<byte, Half>(source.AsSpan());
        channels[0] = (Half)color.Red;
        channels[1] = (Half)color.Green;
        channels[2] = (Half)color.Blue;
        channels[3] = (Half)color.Alpha;
        byte[] destination = new byte[4];
        processor.Apply(source, destination, 1, 1, exposure);
        return destination;
    }
}
