// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// Executable evidence that the colour-managed display transform runs on the GPU and
/// agrees with the CPU export path it is derived from.
/// </summary>
[NotInParallel]
[SupportedOSPlatform("windows")]
public sealed class D3D12DisplayTransformTests
{
    private const uint Width = 32;
    private const uint Height = 24;

    /// <summary>
    /// The bound the GPU lattice is allowed to differ from the CPU processor by, in
    /// 8-bit display code values.
    /// </summary>
    /// <remarks>
    /// The lattice stores display code values at 8 bits and interpolates between them,
    /// so a smooth transform's interpolated value can miss the directly evaluated one
    /// by a small amount. This bound is asserted rather than assumed, and it is far
    /// tighter than the difference an unapplied transform would produce.
    /// </remarks>
    private const int MaximumCodeValueDifference = 2;

    [Test]
    public async Task WarpAppliesConfiguredDisplayTransformToClearedFrame()
    {
        RequireWindows();
        string configPath = RequireTransformableConfigPath();
        var clearColor = new SilkColor(0.25f, 0.5f, 0.75f, 1);
        const float exposure = -1f;

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture display = CreateDisplayTarget(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Width, Height));

        var transform = new RenderDisplayTransform(
            configPath,
            "linear",
            "TestDisplay",
            "TestView");
        _ = renderer.Render(
            display,
            depth,
            new SilkMeshRenderOptions(clearColor, 1)
            {
                DisplayTransform = transform,
                Exposure = exposure,
            });

        byte[] rendered = ReadPixels(display);
        byte[] expected = ApplyCpuProcessor(configPath, clearColor, exposure);

        SilkDisplayTransformDiagnostics diagnostics =
            renderer.DisplayTransformDiagnostics;
        await Assert.That(diagnostics.Status)
            .IsEqualTo(SilkDisplayTransformStatus.Applied);
        await Assert.That(renderer.DisplayTransformDiagnostic).IsNull();
        await Assert.That(diagnostics.Passes).IsEqualTo(1UL);
        await Assert.That(diagnostics.LatticeSize)
            .IsEqualTo(RenderDisplayTransform.DefaultLatticeSize);

        // Every pixel is the transformed clear colour, so the whole image is one
        // analytic value rather than a recorded blob.
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
    public async Task WarpDisplayTransformDiffersFromUntransformedOutput()
    {
        RequireWindows();
        string configPath = RequireTransformableConfigPath();
        var clearColor = new SilkColor(0.25f, 0.5f, 0.75f, 1);

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture display = CreateDisplayTarget(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Width, Height));

        _ = renderer.Render(display, depth, new SilkMeshRenderOptions(clearColor, 1));
        byte[] untransformed = ReadPixels(display);

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

        // The config's display applies an sRGB piecewise power function, which moves
        // every one of these channels far beyond any interpolation tolerance. A pass
        // that quietly did nothing would fail here.
        for (int channel = 0; channel < 3; channel++)
        {
            await Assert.That(Math.Abs(transformed[channel] - untransformed[channel]))
                .IsGreaterThan(16);
        }
    }

    [Test]
    public async Task WarpRestoresUntransformedOutputWhenTransformIsCleared()
    {
        RequireWindows();
        string configPath = RequireTransformableConfigPath();
        var clearColor = new SilkColor(0.25f, 0.5f, 0.75f, 1);

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
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

        _ = renderer.Render(display, depth, new SilkMeshRenderOptions(clearColor, 1));
        byte[] restored = ReadPixels(display);

        await Assert.That(restored.SequenceEqual(baseline)).IsTrue();
        await Assert.That(renderer.DisplayTransformDiagnostics.Status)
            .IsEqualTo(SilkDisplayTransformStatus.Inactive);
    }

    [Test]
    public async Task WarpReportsMissingConfigInsteadOfSilentIdentity()
    {
        RequireWindows();
        var clearColor = new SilkColor(0.25f, 0.5f, 0.75f, 1);
        string missingConfig = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            $"openusd-missing-{Guid.NewGuid():N}.ocio");

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
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
        await Assert.That(renderer.DisplayTransformDiagnostics.Failures)
            .IsEqualTo(1UL);
        RenderDiagnostic? diagnostic = renderer.DisplayTransformDiagnostic;
        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Code).IsEqualTo(
            SilkRenderDiagnosticCodes.DisplayTransformConfigUnavailable);
        await Assert.That(diagnostic.Severity)
            .IsEqualTo(RenderDiagnosticSeverity.Warning);
        await Assert.That(fallback.SequenceEqual(baseline)).IsTrue();
    }

    [Test]
    public async Task WarpReportsUnsupportedViewInsteadOfSilentIdentity()
    {
        RequireWindows();
        string configPath = RequireConfigPath();
        var clearColor = new SilkColor(0.25f, 0.5f, 0.75f, 1);

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
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
                    "NoSuchView"),
            });

        await Assert.That(renderer.DisplayTransformDiagnostics.Status)
            .IsEqualTo(SilkDisplayTransformStatus.TransformUnsupported);
        RenderDiagnostic? diagnostic = renderer.DisplayTransformDiagnostic;
        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Code).IsEqualTo(
            SilkRenderDiagnosticCodes.DisplayTransformUnsupported);
    }

    [Test]
    public async Task WarpReusesLatticeAcrossFramesAndExposureChanges()
    {
        RequireWindows();
        string configPath = RequireTransformableConfigPath();
        var clearColor = new SilkColor(0.25f, 0.5f, 0.75f, 1);
        var transform = new RenderDisplayTransform(
            configPath,
            "linear",
            "TestDisplay",
            "TestView");

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture display = CreateDisplayTarget(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Width, Height));

        for (int frame = 0; frame < 4; frame++)
        {
            _ = renderer.Render(
                display,
                depth,
                new SilkMeshRenderOptions(clearColor, 1)
                {
                    DisplayTransform = transform,
                    Exposure = frame * 0.25f,
                });
        }

        SilkDisplayTransformDiagnostics diagnostics =
            renderer.DisplayTransformDiagnostics;
        await Assert.That(diagnostics.Passes).IsEqualTo(4UL);
        await Assert.That(diagnostics.LatticeBuilds).IsEqualTo(1UL);
        await Assert.That(diagnostics.LatticeUploads).IsEqualTo(1UL);
        await Assert.That(diagnostics.PipelineCreations).IsEqualTo(1UL);
        await Assert.That(diagnostics.BindingCreations).IsEqualTo(1UL);
        await Assert.That(diagnostics.IntermediateCreations).IsEqualTo(1UL);
        await Assert.That(diagnostics.ParameterUploads).IsEqualTo(4UL);
    }

    [Test]
    public async Task WarpRebuildsDisplayTransformResourcesAfterDeviceInvalidation()
    {
        RequireWindows();
        string configPath = RequireTransformableConfigPath();
        var clearColor = new SilkColor(0.25f, 0.5f, 0.75f, 1);
        var transform = new RenderDisplayTransform(
            configPath,
            "linear",
            "TestDisplay",
            "TestView");

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture display = CreateDisplayTarget(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Width, Height));

        _ = renderer.Render(
            display,
            depth,
            new SilkMeshRenderOptions(clearColor, 1) { DisplayTransform = transform });
        byte[] before = ReadPixels(display);
        D3D12DisplayTransformNativeStatistics initial =
            device.DisplayTransformNativeStatisticsForTesting;

        device.InvalidateSelectionOutlineDeviceGenerationForTesting();

        _ = renderer.Render(
            display,
            depth,
            new SilkMeshRenderOptions(clearColor, 1) { DisplayTransform = transform });
        byte[] after = ReadPixels(display);
        D3D12DisplayTransformNativeStatistics rebuilt =
            device.DisplayTransformNativeStatisticsForTesting;
        SilkDisplayTransformDiagnostics diagnostics =
            renderer.DisplayTransformDiagnostics;

        await Assert.That(rebuilt.DeviceGeneration)
            .IsGreaterThan(initial.DeviceGeneration);
        await Assert.That(rebuilt.PipelineCreations)
            .IsEqualTo(initial.PipelineCreations + 1);
        await Assert.That(rebuilt.BindingCreations)
            .IsEqualTo(initial.BindingCreations + 1);

        // The generation change releases the previous pipeline and binding rather than
        // leaking them alongside their replacements.
        await Assert.That(rebuilt.ActivePipelines).IsEqualTo(initial.ActivePipelines);
        await Assert.That(rebuilt.ActiveBindings).IsEqualTo(initial.ActiveBindings);
        await Assert.That(diagnostics.DeviceInvalidations).IsEqualTo(1UL);
        await Assert.That(diagnostics.Status)
            .IsEqualTo(SilkDisplayTransformStatus.Applied);

        // The rebuilt resources still produce the same image, and the lattice was reused
        // from the cache rather than baked again.
        await Assert.That(after.SequenceEqual(before)).IsTrue();
        await Assert.That(diagnostics.LatticeBuilds).IsEqualTo(1UL);
    }

    [Test]
    public async Task WarpReleasesEveryDisplayTransformResourceOnDisposal()
    {
        RequireWindows();
        string configPath = RequireTransformableConfigPath();
        var clearColor = new SilkColor(0.25f, 0.5f, 0.75f, 1);

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using ISilkGraphicsTexture display = CreateDisplayTarget(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Width, Height));

        var renderer = new SilkMeshRenderer(device);
        try
        {
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
            await Assert.That(
                    device.DisplayTransformNativeStatisticsForTesting.ActivePipelines)
                .IsEqualTo(1L);
            await Assert.That(
                    device.DisplayTransformNativeStatisticsForTesting.ActiveBindings)
                .IsEqualTo(1L);
        }
        finally
        {
            renderer.Dispose();
        }

        await Assert.That(
                device.DisplayTransformNativeStatisticsForTesting.ActivePipelines)
            .IsEqualTo(0L);
        await Assert.That(
                device.DisplayTransformNativeStatisticsForTesting.ActiveBindings)
            .IsEqualTo(0L);
    }

    [Test]
    public async Task WarpRetainedCaptureAppliesTheDisplayTransformWithoutADoubleConversion()
    {
        RequireWindows();
        string configPath = RequireTransformableConfigPath();
        var clearColor = new SilkColor(0.25f, 0.5f, 0.75f, 1);
        const float exposure = -1f;
        var transform = new RenderDisplayTransform(
            configPath,
            "linear",
            "TestDisplay",
            "TestView");

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using var renderer = new SilkMeshRenderer(device);
        RenderSettings settings = new RenderSettings(
            1,
            enableLighting: true,
            enableShadows: true,
            new Vector4(clearColor.Red, clearColor.Green, clearColor.Blue, clearColor.Alpha),
            backfaceCulling: true,
            useSceneMaterials: true,
            RenderComplexity.Low,
            RenderOutputTransform.Identity,
            exposure) with
        {
            DisplayTransform = transform,
        };

        SilkFrameCaptureResult capture = SilkFrameCapture.CaptureRetained(
            renderer,
            device,
            (int)Width,
            (int)Height,
            settings);
        byte[] expected = ApplyCpuProcessor(configPath, clearColor, exposure);

        await Assert.That(capture.Width).IsEqualTo((int)Width);
        await Assert.That(capture.Height).IsEqualTo((int)Height);
        byte[] pixels = capture.Rgba.ToArray();
        for (int pixel = 0; pixel < pixels.Length; pixel += 4)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                await Assert.That(Math.Abs(pixels[pixel + channel] - expected[channel]))
                    .IsLessThanOrEqualTo(MaximumCodeValueDifference);
            }
        }
        await Assert.That(renderer.DisplayTransformDiagnostics.Status)
            .IsEqualTo(SilkDisplayTransformStatus.Applied);

        // A capture that also handed the CPU export processor the same frame would colour
        // manage it twice, so the combination is refused rather than quietly accepted.
        using SilkOpenColorIoProcessor processor = new SilkOpenColorIoDisplayTransform(
            configPath,
            "linear",
            "TestDisplay",
            "TestView").CreateProcessor();
        await Assert.That(() => SilkFrameCapture.CaptureRetained(
                renderer,
                device,
                (int)Width,
                (int)Height,
                settings,
                processor))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task WarpDisplayTransformPreservesVerticalOrientation()
    {
        RequireWindows();
        string configPath = RequireTransformableConfigPath();

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture display = CreateDisplayTarget(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Width, Height));
        DisplayTransformConformance.ApplyVerticallyAsymmetricScene(renderer);

        var options = new SilkMeshRenderOptions(new SilkColor(0, 0, 0, 1), 1);
        _ = renderer.Render(display, depth, options);
        byte[] untransformed = ReadPixels(display);
        (double UntransformedTop, double UntransformedBottom) baseline =
            DisplayTransformConformance.MeasureVerticalBias(untransformed);

        _ = renderer.Render(
            display,
            depth,
            options with
            {
                // The identity view maps the working space to itself, so the pass is a
                // pure blit: any vertical difference from the untransformed frame is an
                // orientation fault and nothing else.
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
        await Assert.That(baseline.UntransformedTop)
            .IsGreaterThan(baseline.UntransformedBottom + 64);
        await Assert.That(measured.Top).IsGreaterThan(measured.Bottom + 64);
        await Assert.That(measured.Top).IsEqualTo(baseline.UntransformedTop).Within(3);
        await Assert.That(measured.Bottom)
            .IsEqualTo(baseline.UntransformedBottom).Within(3);
    }

    [Test]
    public async Task WarpLookOverrideMatchesTheCpuExportPath()
    {
        RequireWindows();
        string configPath = RequireLookConfigPath();
        var clearColor = new SilkColor(0.5f, 0.5f, 0.5f, 1);

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture display = CreateDisplayTarget(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Width, Height));

        // The view declares its own look and the override's process space is not the
        // source space, so this is exactly the configuration that a look composed in the
        // wrong space, or composed with the view instead of replacing it, gets wrong.
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
        byte[] expected = ApplyCpuLookProcessor(configPath, clearColor);

        await Assert.That(renderer.DisplayTransformDiagnostics.Status)
            .IsEqualTo(SilkDisplayTransformStatus.Applied);
        for (int channel = 0; channel < 3; channel++)
        {
            await Assert.That(Math.Abs(rendered[channel] - expected[channel]))
                .IsLessThanOrEqualTo(MaximumCodeValueDifference);
        }

        // 0.5 linear -> look_space 0.125 -> ^2 -> reference -> display = 0.03125.
        await Assert.That(Math.Abs(rendered[0] - (int)Math.Round(0.03125 * 255)))
            .IsLessThanOrEqualTo(MaximumCodeValueDifference);
    }

    [Test]
    public async Task WarpReuploadsTheLatticeWhenTheConfigChangesBetweenRenders()
    {
        RequireWindows();
        string sourceConfig = RequireTransformableConfigPath();
        var clearColor = new SilkColor(0.25f, 0.5f, 0.75f, 1);
        string root = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            $"warp-config-edit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string configPath = Path.Combine(root, "config.ocio");
        try
        {
            File.Copy(sourceConfig, configPath);

            using D3D12SilkGraphicsDevice device =
                D3D12SilkGraphicsDevice.Create(useWarp: true);
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
            await Assert.That(renderer.DisplayTransformDiagnostics.LatticeUploads)
                .IsEqualTo(1UL);

            // The transform descriptor is byte-for-byte identical across this edit: the
            // path, the display, the view, the lattice size and the shaper are all
            // unchanged. Only the file's contents moved. Keying the uploaded texture on
            // the request rather than on the baked lattice left the old bytes on the GPU
            // and produced the previous image from a rebaked lattice.
            File.WriteAllText(
                configPath,
                File.ReadAllText(configPath)
                    .Replace("gamma: 2.4", "gamma: 1.6", StringComparison.Ordinal));

            // Past the documented revalidation window, so this exercises the production
            // path -- the shared identity provider's real throttle -- rather than a test
            // seam that bypasses it.
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
            await Assert.That(diagnostics.LatticeBuilds).IsEqualTo(2UL);
            await Assert.That(after.SequenceEqual(before)).IsFalse();

            // The intermediate, the pipeline, and the binding are unaffected by a
            // content change of the same shape, so only the upload repeats.
            await Assert.That(diagnostics.IntermediateCreations).IsEqualTo(1UL);
            await Assert.That(diagnostics.PipelineCreations).IsEqualTo(1UL);
            await Assert.That(diagnostics.BindingCreations).IsEqualTo(1UL);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        PrependNativeSearchPath();
    }

    private static void PrependNativeSearchPath()
    {
        string? root = FindRepositoryRoot();
        if (root is null)
        {
            return;
        }

        // The colour-management library lives beside the project-owned C ABI shim in
        // the native install tree, not in the test output, so the loader is pointed at
        // it exactly the way the parity harness points at hdSilk.
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

    private static string RequireConfigPath()
    {
        string root = FindRepositoryRoot() ??
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string path = Path.Combine(root, "test-assets", "ocio-test-config.ocio");
        if (!File.Exists(path))
        {
            Skip.Test($"The OpenColorIO test config is unavailable at {path}.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        return path;
    }

    private static string RequireTransformableConfigPath()
    {
        string path = RequireConfigPath();
        try
        {
            _ = SilkOpenColorIoLatticeProvider.Shared.Create(
                new RenderDisplayTransform(path, "linear", "TestDisplay", "TestView"));
        }
        catch (SilkDisplayTransformException exception)
        {
            Skip.Test(
                "The OpenColorIO native runtime is unavailable in this host: " +
                exception.Message);
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
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
            Skip.Test($"The OpenColorIO look config is unavailable at {path}.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        try
        {
            _ = SilkOpenColorIoLatticeProvider.Shared.Create(
                new RenderDisplayTransform(
                    path,
                    "linear",
                    "LookDisplay",
                    "ViewWithLook",
                    "OverrideLook"));
        }
        catch (SilkDisplayTransformException exception)
        {
            Skip.Test(
                "The OpenColorIO native runtime is unavailable in this host: " +
                exception.Message);
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        return path;
    }

    private static byte[] ApplyCpuLookProcessor(string configPath, SilkColor color)
    {
        var transform = new SilkOpenColorIoDisplayTransform(
            configPath,
            "linear",
            "LookDisplay",
            "ViewWithLook",
            "OverrideLook");
        using SilkOpenColorIoProcessor processor = transform.CreateProcessor();
        byte[] source = new byte[8];
        Span<Half> channels = MemoryMarshal.Cast<byte, Half>(source.AsSpan());
        channels[0] = (Half)color.Red;
        channels[1] = (Half)color.Green;
        channels[2] = (Half)color.Blue;
        channels[3] = (Half)color.Alpha;
        byte[] destination = new byte[4];
        processor.Apply(source, destination, 1, 1, 0f);
        return destination;
    }

    private static ISilkGraphicsTexture CreateDisplayTarget(
        D3D12SilkGraphicsDevice device) =>
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
