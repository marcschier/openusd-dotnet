// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Runtime.CompilerServices;
using OpenUsd.Render;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkDepthCaptureNativeTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    [Arguments(SilkGraphicsBackend.Metal, false)]
    [Arguments(SilkGraphicsBackend.Metal, true)]
    public async Task IncrementalCapturerKeepsDepthAcrossColorCapturesAndTimeEdits(
        SilkGraphicsBackend backend, bool perspective)
    {
        string plugins = RequirePlugins();
        string root = Directory.CreateTempSubdirectory("openusd-depth-native-").FullName;
        try
        {
            string path = Path.Combine(root, "planes.usda");
            File.WriteAllText(path, CreateScene(perspective));
            CameraState camera = CreateCamera(perspective);
            SilkFrameCaptureResult firstColor;
            SilkFrameCaptureResult first;
            SilkFrameCaptureResult editedColor;
            SilkFrameCaptureResult edited;
            SilkFrameCaptureResult repeated;
            SilkFrameCaptureResult ocioColor;
            SilkFrameCaptureResult ocioDepth;
            using (ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend))
            using (OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins, path))
            using (var capturer = new SilkFrameCapturer(device))
            using (SilkOpenColorIoProcessor processor = CreateOcioProcessor())
            {
                firstColor = capturer.Capture(session, 40, 32, camera: camera);
                first = capturer.CaptureWithDepth(
                    session, 40, 32, RenderSettings.Default, camera: camera);
                editedColor = capturer.Capture(session, 40, 32, timeCode: 1, camera: camera);
                edited = capturer.CaptureWithDepth(
                    session, 40, 32, RenderSettings.Default, timeCode: 1, camera: camera);
                repeated = capturer.CaptureWithDepth(
                    session, 40, 32, RenderSettings.Default, timeCode: 1, camera: camera);
                ocioColor = capturer.Capture(
                    session, 40, 32, RenderSettings.Default, processor, timeCode: 1, camera: camera);
                ocioDepth = capturer.CaptureWithDepth(
                    session, 40, 32, RenderSettings.Default, processor, timeCode: 1, camera: camera);
            }

            await Assert.That(firstColor.Rgba.Span.SequenceEqual(first.Rgba.Span)).IsTrue();
            await Assert.That(editedColor.Rgba.Span.SequenceEqual(edited.Rgba.Span)).IsTrue();
            await Assert.That(firstColor.Depth).IsNull();
            await Assert.That(first.Depth!.Values.Span[(8 * 40) + 10])
                .IsEqualTo(perspective ? 0.733333333f : 0.2f).Within(0.00001f);
            await Assert.That(edited.Depth!.Values.Span[(8 * 40) + 10])
                .IsEqualTo(perspective ? 0.825f : 0.3f).Within(0.00001f);
            await Assert.That(edited.Depth.Values.Span[(24 * 40) + 30])
                .IsEqualTo(perspective ? 0.942857143f : 0.6f).Within(0.00001f);
            await Assert.That(first.Depth.Values.Span[(24 * 40) + 10]).IsEqualTo(1f);
            await Assert.That(first.Rgba.Span[((8 * 40) + 10) * 4]).IsGreaterThan((byte)150);
            await Assert.That(edited.Rgba.Span[(((8 * 40) + 10) * 4) + 2]).IsGreaterThan((byte)150);
            await Assert.That(repeated.Depth!.Values.Span.SequenceEqual(edited.Depth.Values.Span)).IsTrue();
            await Assert.That(repeated.RenderResult.DrawCount).IsEqualTo(2);
            await Assert.That(ocioColor.Rgba.Span.SequenceEqual(ocioDepth.Rgba.Span)).IsTrue();
            await Assert.That(ocioDepth.Depth!.Values.Span.SequenceEqual(edited.Depth.Values.Span)).IsTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task RetainedDepthIsNotTransformedOrPaintedByOcioAndSelection(
        SilkGraphicsBackend backend)
    {
        _ = RequirePlugins();
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var renderer = new SilkMeshRenderer(device);
        using SilkOpenColorIoProcessor processor = CreateOcioProcessor();
        SilkMeshRendererConformance.Apply(
            renderer, 1, SilkDepthCaptureConformance.CreateFrame(perspective: false),
            SilkMeshRendererConformance.CreateMeshCommand(
                1, "/Plane",
                [-0.8f, 0.1f, -1, -0.1f, 0.1f, -1, -0.1f, 0.8f, -1, -0.8f, 0.8f, -1],
                [0, 1, 2, 0, 2, 3],
                color: [0.25f, 0.5f, 0.125f, 1]));
        var settings = new RenderSettings(
            1, true, true, new Vector4(0, 0, 0, 1), true, true,
            RenderComplexity.Low, RenderOutputTransform.Identity, -1);
        SilkFrameCaptureResult plain = SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, 40, 32, settings);
        renderer.UpdateSelection(new SelectionState(["/Plane"]));
        SilkFrameCaptureResult ocioColor = SilkFrameCapture.CaptureRetained(
            renderer, device, 40, 32, settings, processor);
        SilkFrameCaptureResult ocioDepth = SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, 40, 32, settings, processor);
        RenderSettings gpuSettings = settings with
        {
            DisplayTransform = new RenderDisplayTransform(
                FindOcioConfig(), "linear", "TestDisplay", "TestView")
        };
        SilkFrameCaptureResult gpuColor = SilkFrameCapture.CaptureRetained(
            renderer, device, 40, 32, gpuSettings);
        SilkFrameCaptureResult gpuDepth = SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, 40, 32, gpuSettings);
        SilkDisplayTransformStatus displayStatus = renderer.DisplayTransformDiagnostics.Status;
        SilkSelectionOutlineStatus selectionStatus = renderer.SelectionOutlineDiagnostics.Status;
        renderer.Dispose();
        device.Dispose();

        await Assert.That(ocioColor.Rgba.Span.SequenceEqual(ocioDepth.Rgba.Span)).IsTrue();
        await Assert.That(gpuColor.Rgba.Span.SequenceEqual(gpuDepth.Rgba.Span)).IsTrue();
        await Assert.That(ocioDepth.Depth!.Values.Span.SequenceEqual(plain.Depth!.Values.Span)).IsTrue();
        await Assert.That(gpuDepth.Depth!.Values.Span.SequenceEqual(plain.Depth.Values.Span)).IsTrue();
        await Assert.That(displayStatus).IsEqualTo(SilkDisplayTransformStatus.Applied);
        await Assert.That(selectionStatus).IsEqualTo(SilkSelectionOutlineStatus.Rendered);
        await Assert.That(plain.Rgba.Span.SequenceEqual(ocioDepth.Rgba.Span)).IsFalse();
        int paintedBackground = 0;
        for (int pixel = 0; pixel < plain.Depth.Values.Length; pixel++)
        {
            if (plain.Depth.Values.Span[pixel] == 1 &&
                !plain.Rgba.Span.Slice(pixel * 4, 4).SequenceEqual(
                    gpuDepth.Rgba.Span.Slice(pixel * 4, 4)))
            {
                paintedBackground++;
            }
        }
        await Assert.That(paintedBackground).IsGreaterThan(0)
            .Because("selection can paint visible pixels while their real depth remains clear");
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    [Arguments(SilkGraphicsBackend.Metal, false)]
    [Arguments(SilkGraphicsBackend.Metal, true)]
    public async Task SelectionUploadByteQuotaIsAppliedBeforeTargetsAndNativeSync(
        SilkGraphicsBackend backend, bool cpuOcio)
    {
        string plugins = RequirePlugins();
        string root = Directory.CreateTempSubdirectory("openusd-depth-upload-quota-").FullName;
        try
        {
            string path = Path.Combine(root, "planes.usda");
            File.WriteAllText(path, CreateScene(perspective: false));
            using var device = new SilkDepthCaptureObservedDevice(
                SilkDepthCaptureConformance.CreateDevice(backend));
            using var renderer = new SilkMeshRenderer(device);
            using SilkOpenColorIoProcessor processor = CreateOcioProcessor();
            SilkMeshRendererConformance.Apply(
                renderer, 1, SilkDepthCaptureConformance.CreateFrame(perspective: false),
                SilkMeshRendererConformance.CreateMeshCommand(
                    1, "/Plane",
                    [-0.8f, 0.1f, -1, -0.1f, 0.1f, -1, -0.1f, 0.8f, -1, -0.8f, 0.8f, -1],
                    [0, 1, 2, 0, 2, 3], color: [0.25f, 0.5f, 0.125f, 1]));
            renderer.UpdateSelection(new SelectionState(["/Plane"]));
            // 1280 pixels require 25,600 managed bytes, including the 5,120-byte
            // RGBA selection-upload copy. The former 16-byte charge admits 25,599.
            var insufficient = new SilkDepthCaptureOptions(maximumReadbackBytes: 25_599);
            var exact = new SilkDepthCaptureOptions(maximumReadbackBytes: 25_600);
            int beforeRetained = device.TextureRequests;
            bool retainedRefused = false;
            try
            {
                _ = cpuOcio
                    ? SilkFrameCapture.CaptureRetainedWithDepth(
                        renderer, device, 40, 32, RenderSettings.Default, processor, insufficient)
                    : SilkFrameCapture.CaptureRetainedWithDepth(
                        renderer, device, 40, 32, RenderSettings.Default, insufficient);
            }
            catch (ArgumentOutOfRangeException)
            {
                retainedRefused = true;
            }
            int afterRetained = device.TextureRequests;

            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins, path);
            using var capturer = new SilkFrameCapturer(device);
            int beforeSync = device.TextureRequests;
            bool syncRefused = false;
            try
            {
                _ = cpuOcio
                    ? capturer.CaptureWithDepth(
                        session, 40, 32, RenderSettings.Default, processor, insufficient,
                        camera: CreateCamera(perspective: false))
                    : capturer.CaptureWithDepth(
                        session, 40, 32, RenderSettings.Default, insufficient,
                        camera: CreateCamera(perspective: false));
            }
            catch (ArgumentOutOfRangeException)
            {
                syncRefused = true;
            }
            int afterSync = device.TextureRequests;

            SilkFrameCaptureResult selectedColor = cpuOcio
                ? SilkFrameCapture.CaptureRetained(
                    renderer, device, 40, 32, RenderSettings.Default, processor)
                : SilkFrameCapture.CaptureRetained(
                    renderer, device, 40, 32, RenderSettings.Default);
            SilkFrameCaptureResult selectedDepth = cpuOcio
                ? SilkFrameCapture.CaptureRetainedWithDepth(
                    renderer, device, 40, 32, RenderSettings.Default, processor, exact)
                : SilkFrameCapture.CaptureRetainedWithDepth(
                    renderer, device, 40, 32, RenderSettings.Default, exact);
            SilkSelectionOutlineStatus selectionStatus = renderer.SelectionOutlineDiagnostics.Status;

            // A fresh renderer must still receive both native meshes: admission
            // must not consume the initial session delta before refusing the frame.
            using var nativeRenderer = new SilkMeshRenderer(device);
            using ISilkGraphicsTexture color = device.CreateTexture2D(
                SilkTextureDescriptor.HdrColorTarget(40, 32));
            using ISilkGraphicsTexture depth = device.CreateTexture2D(
                SilkTextureDescriptor.SampledDepthTarget(40, 32));
            using OpenUsdSilkPage page = session.Sync(
                40, 32, camera: CreateCamera(perspective: false));
            SilkMeshRenderResult nativeResult = nativeRenderer.ApplyAndRender(page, color, depth);
            var nativeDepth = new float[40 * 32];
            depth.ReadbackForTesting(nativeDepth);

            await Assert.That(retainedRefused).IsTrue();
            await Assert.That(afterRetained).IsEqualTo(beforeRetained);
            await Assert.That(syncRefused).IsTrue();
            await Assert.That(afterSync).IsEqualTo(beforeSync);
            await Assert.That(selectionStatus).IsEqualTo(SilkSelectionOutlineStatus.Rendered);
            await Assert.That(selectedDepth.Rgba.Span.SequenceEqual(selectedColor.Rgba.Span)).IsTrue();
            await Assert.That(selectedDepth.Depth!.Values.Span[(8 * 40) + 10])
                .IsEqualTo(0.2f).Within(0.00001f);
            await Assert.That(nativeResult.DrawCount).IsEqualTo(2);
            await Assert.That(nativeDepth[(8 * 40) + 10]).IsEqualTo(0.2f).Within(0.00001f);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    [Arguments(SilkGraphicsBackend.Metal, false)]
    [Arguments(SilkGraphicsBackend.Metal, true)]
    public async Task CroppedAuthoredProductKeepsLiteralDepthAndRasterRows(
        SilkGraphicsBackend backend, bool perspective)
    {
        string plugins = RequirePlugins();
        string root = Directory.CreateTempSubdirectory("openusd-depth-crop-").FullName;
        try
        {
            string path = Path.Combine(root, "planes.usda");
            File.WriteAllText(path, CreateScene(perspective));
            byte[] original = File.ReadAllBytes(path);
            RenderPreparedFrame full;
            RenderPreparedFrame crop;
            using (UsdStage stage = UsdStage.Open(path))
            {
                UsdRenderSpecification specification = stage.GetRenderSpecification()
                    ?? throw new InvalidOperationException("The depth fixture has no render specification.");
                full = new RenderProductRequest(specification, 0, new RenderProductOverrides(
                    dataWindowNdc: new UsdVec4f(0, 0, 1, 1))).PrepareFrame(stage, 0);
                crop = new RenderProductRequest(specification, 0).PrepareFrame(stage, 0);
            }
            SilkFrameCaptureResult fullImage;
            SilkFrameCaptureResult croppedImage;
            SilkFrameCaptureResult merelyResized;
            using (ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend))
            using (OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins, path))
            using (var capturer = new SilkFrameCapturer(device))
            {
                fullImage = capturer.CaptureWithDepth(
                    session, 96, 96, RenderSettings.Default, camera: full.Camera);
                croppedImage = capturer.CaptureWithDepth(
                    session, 60, 36, RenderSettings.Default, camera: crop.Camera);
                merelyResized = capturer.CaptureWithDepth(
                    session, 60, 36, RenderSettings.Default, camera: full.Camera);
            }

            await Assert.That(crop.OutputDimensions).IsEqualTo(new ViewportDimensions(60, 36));
            await Assert.That(crop.DataWindowMinX).IsEqualTo(12);
            await Assert.That(crop.DataWindowMinY).IsEqualTo(24);
            // The authored lower-left data window maps to full-raster top row 36.
            var expected = new float[60 * 36];
            for (int row = 0; row < 36; row++)
            {
                fullImage.Depth!.Values.Span.Slice(((36 + row) * 96) + 12, 60)
                    .CopyTo(expected.AsSpan(row * 60, 60));
            }
            float[] actual = croppedImage.Depth!.Values.ToArray();
            await Assert.That(actual.Length).IsEqualTo(expected.Length);
            await Assert.That(actual.Zip(expected, (left, right) => MathF.Abs(left - right)).Max())
                .IsLessThan(0.00001f);
            await Assert.That(actual[(4 * 60) + 12])
                .IsEqualTo(perspective ? 0.733333333f : 0.2f).Within(0.00001f);
            await Assert.That(actual[(24 * 60) + 48])
                .IsEqualTo(perspective ? 0.942857143f : 0.6f).Within(0.00001f);
            await Assert.That(actual[(24 * 60) + 12]).IsEqualTo(1f);
            await Assert.That(actual.Count(value => value < 1)).IsGreaterThan(100);
            await Assert.That(merelyResized.Depth!.Values.Span.SequenceEqual(actual)).IsFalse()
                .Because("resizing the full camera is not a product-camera crop");
            await Assert.That(File.ReadAllBytes(path).SequenceEqual(original)).IsTrue();
            await Assert.That(File.Exists(Path.Combine(root, "not-written.exr"))).IsFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task FreshCapturerCannotConsumeAnotherRenderersIncrementalSession(
        SilkGraphicsBackend backend)
    {
        string plugins = RequirePlugins();
        string root = Directory.CreateTempSubdirectory("openusd-depth-ownership-").FullName;
        try
        {
            string path = Path.Combine(root, "planes.usda");
            File.WriteAllText(path, CreateScene(perspective: false));
            InvalidOperationException? refusal = null;
            SilkFrameCaptureResult retained;
            using (ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend))
            using (OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins, path))
            using (var owner = new SilkFrameCapturer(device))
            using (var fresh = new SilkFrameCapturer(device))
            {
                CameraState camera = CreateCamera(perspective: false);
                _ = owner.Capture(session, 40, 32, camera: camera);
                try
                {
                    _ = fresh.CaptureWithDepth(
                        session, 40, 32, RenderSettings.Default, camera: camera);
                }
                catch (InvalidOperationException exception)
                {
                    refusal = exception;
                }
                retained = owner.CaptureWithDepth(
                    session, 40, 32, RenderSettings.Default, camera: camera);
            }

            await Assert.That(refusal).IsNotNull()
                .Because("an empty incremental session must not masquerade as a clear-depth scene");
            await Assert.That(retained.RenderResult.DrawCount).IsEqualTo(2);
            await Assert.That(retained.Depth!.Values.Span[(8 * 40) + 10])
                .IsEqualTo(0.2f).Within(0.00001f);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static CameraState CreateCamera(bool perspective) =>
        new(
            Matrix4x4.CreateTranslation(0, 0, -2),
            perspective
                ? new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -1.2f, -1, 0, 0, -2.2f, 0)
                : new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -0.2f, 0, 0, 0, -1.2f, 1));

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task QuotasAndCancellationNeverLoseAnIncrementalSceneUpdate(
        SilkGraphicsBackend backend)
    {
        string plugins = RequirePlugins();
        string root = Directory.CreateTempSubdirectory("openusd-depth-cancellation-").FullName;
        try
        {
            string path = Path.Combine(root, "planes.usda");
            File.WriteAllText(path, CreateScene(perspective: false));
            bool quotaRefused = false;
            bool preCancelled = false;
            bool submittedCancellation = false;
            int targetsBefore;
            int targetsAfterRefusals;
            int submitted;
            int completed;
            bool stayedOnCallingThread;
            SilkFrameCaptureResult first;
            SilkFrameCaptureResult recovered;
            using (var device = new SilkDepthCaptureObservedDevice(
                SilkDepthCaptureConformance.CreateDevice(backend)))
            using (OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins, path))
            using (var capturer = new SilkFrameCapturer(device))
            using (var cancellation = new CancellationTokenSource())
            {
                CameraState camera = CreateCamera(perspective: false);
                first = capturer.CaptureWithDepth(
                    session, 40, 32, RenderSettings.Default, camera: camera);
                targetsBefore = device.TextureRequests;
                try
                {
                    _ = capturer.CaptureWithDepth(
                        session, 40, 32, RenderSettings.Default,
                        new SilkDepthCaptureOptions(maximumPixelCount: 1),
                        timeCode: 1, camera: camera);
                }
                catch (ArgumentOutOfRangeException)
                {
                    quotaRefused = true;
                }
                try
                {
                    _ = capturer.CaptureWithDepth(
                        session, 40, 32, RenderSettings.Default, timeCode: 1, camera: camera,
                        cancellationToken: new CancellationToken(canceled: true));
                }
                catch (OperationCanceledException)
                {
                    preCancelled = true;
                }
                targetsAfterRefusals = device.TextureRequests;
                device.AfterCompletedWait = cancellation.Cancel;
                try
                {
                    _ = capturer.CaptureWithDepth(
                        session, 40, 32, RenderSettings.Default, timeCode: 1, camera: camera,
                        cancellationToken: cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    submittedCancellation = true;
                }
                recovered = capturer.CaptureWithDepth(
                    session, 40, 32, RenderSettings.Default, timeCode: 1, camera: camera);
                submitted = device.Submissions;
                completed = device.CompletedSubmissions;
                stayedOnCallingThread = device.StayedOnCallingThread;
            }

            await Assert.That(quotaRefused).IsTrue();
            await Assert.That(preCancelled).IsTrue();
            await Assert.That(submittedCancellation).IsTrue();
            await Assert.That(targetsAfterRefusals).IsEqualTo(targetsBefore);
            await Assert.That(submitted).IsGreaterThan(0);
            await Assert.That(completed).IsEqualTo(submitted);
            await Assert.That(stayedOnCallingThread).IsTrue();
            await Assert.That(first.Depth!.Values.Span[(8 * 40) + 10])
                .IsEqualTo(0.2f).Within(0.00001f);
            await Assert.That(recovered.Depth!.Values.Span[(8 * 40) + 10])
                .IsEqualTo(0.3f).Within(0.00001f);
            await Assert.That(recovered.Rgba.Span[(((8 * 40) + 10) * 4) + 2])
                .IsGreaterThan((byte)150);
            await Assert.That(recovered.RenderResult.DrawCount).IsEqualTo(2);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static SilkOpenColorIoProcessor CreateOcioProcessor() =>
        new SilkOpenColorIoDisplayTransform(
            FindOcioConfig(), "linear", "TestDisplay", "TestView").CreateProcessor();

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task CaptureDoesNotTakeOwnershipOfTheCallersNativeSessionLifetime(
        SilkGraphicsBackend backend)
    {
        string plugins = RequirePlugins();
        string root = Directory.CreateTempSubdirectory("openusd-depth-session-lifetime-").FullName;
        try
        {
            string path = Path.Combine(root, "planes.usda");
            File.WriteAllText(path, CreateScene(perspective: false));
            using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
            using var capturer = new SilkFrameCapturer(device);
            WeakReference<OpenUsdSilkSession> session = CaptureUnrootedSession(capturer, plugins, path);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            bool remainsAlive = session.TryGetTarget(out _);
            GC.KeepAlive(capturer);
            await Assert.That(remainsAlive).IsFalse()
                .Because("tracking capture continuity must not retain the caller's native session");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<OpenUsdSilkSession> CaptureUnrootedSession(
        SilkFrameCapturer capturer, string plugins, string path)
    {
        OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins, path);
        _ = capturer.CaptureWithDepth(
            session, 40, 32, RenderSettings.Default, camera: CreateCamera(perspective: false));
        return new WeakReference<OpenUsdSilkSession>(session);
    }

    private static string FindOcioConfig()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory, "depth-capture", "ocio-test-config.ocio");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The checked depth-capture OCIO fixture was not copied.", path);
        }
        return path;
    }

    private static string CreateScene(bool perspective) =>
        $$"""
        #usda 1.0
        (
            defaultPrim = "World"
            upAxis = "Y"
            metersPerUnit = 1
            renderSettingsPrimPath = "/Settings"
        )
        def Xform "World"
        {
            def Camera "Camera"
            {
                token projection = "{{(perspective ? "perspective" : "orthographic")}}"
                float horizontalAperture = 20
                float verticalAperture = 20
                float focalLength = 10
                float2 clippingRange = (1, 11)
                double3 xformOp:translate = (0, 0, 2)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            def Mesh "Near"
            {
                uniform token subdivisionScheme = "none"
                uniform bool doubleSided = 1
                point3f[] points = {{(perspective
                    ? "[(-2.7, 0.3, -1), (-0.3, 0.3, -1), (-0.3, 2.7, -1), (-2.7, 2.7, -1)]"
                    : "[(-0.9, 0.1, -1), (-0.1, 0.1, -1), (-0.1, 0.9, -1), (-0.9, 0.9, -1)]")}}
                int[] faceVertexCounts = [4]
                int[] faceVertexIndices = [0, 1, 2, 3]
                color3f[] primvars:displayColor.timeSamples = { 0: [(1, 0, 0)], 1: [(0, 0, 1)] }
                uniform token primvars:displayColor:interpolation = "constant"
                double3 xformOp:translate.timeSamples = { 0: (0, 0, 0), 1: (0, 0, -1) }
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            def Mesh "Far"
            {
                uniform token subdivisionScheme = "none"
                uniform bool doubleSided = 1
                point3f[] points = {{(perspective
                    ? "[(0.7, -6.3, -5), (6.3, -6.3, -5), (6.3, -0.7, -5), (0.7, -0.7, -5)]"
                    : "[(0.1, -0.9, -5), (0.9, -0.9, -5), (0.9, -0.1, -5), (0.1, -0.1, -5)]")}}
                int[] faceVertexCounts = [4]
                int[] faceVertexIndices = [0, 1, 2, 3]
                color3f[] primvars:displayColor = [(0, 1, 0)]
                uniform token primvars:displayColor:interpolation = "constant"
            }
        }
        def RenderSettings "Settings"
        {
            rel camera = </World/Camera>
            rel products = </Product>
            int2 resolution = (96, 96)
            float4 dataWindowNDC = (0.125, 0.25, 0.75, 0.625)
        }
        def RenderProduct "Product"
        {
            token productName = "not-written.exr"
            rel orderedVars = </Color>
        }
        def RenderVar "Color"
        {
            token dataType = "color3f"
            string sourceName = "color"
            token sourceType = "raw"
        }
        """;

    private static string RequirePlugins()
    {
        string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
        if (string.IsNullOrWhiteSpace(plugins))
        {
            Skip.Test("Set OPENUSD_TEST_PLUGIN_PATH to the complete matched ABI23 runtime plugin directory.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        return plugins;
    }
}
