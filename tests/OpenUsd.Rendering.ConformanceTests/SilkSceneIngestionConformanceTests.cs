// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkSceneIngestionConformanceTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task ExplicitSceneIngestionChangesVisibilityAndMaterialBinding(SilkGraphicsBackend backend)
    {
        using var fixture = new SceneFixture();
        byte[] source = File.ReadAllBytes(fixture.ScenePath);

        using OpenUsdSilkSession session = fixture.CreateSession();
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var renderer = new SilkMeshRenderer(device);

        CaptureResult full = Capture(
            device,
            renderer,
            session,
            new SilkSceneIngestionOptions(
                RenderPurpose.Default | RenderPurpose.Render,
                "full"));
        CaptureResult fullRepeat = Capture(
            device,
            renderer,
            session,
            new SilkSceneIngestionOptions(
                RenderPurpose.Default | RenderPurpose.Render,
                "full"));
        CaptureResult preview = Capture(
            device,
            renderer,
            session,
            new SilkSceneIngestionOptions(
                RenderPurpose.Default | RenderPurpose.Render,
                "preview"));
        CaptureResult fullRestore = Capture(
            device,
            renderer,
            session,
            new SilkSceneIngestionOptions(
                RenderPurpose.Default | RenderPurpose.Render,
                "full"));
        CaptureResult legacy = CaptureLegacy(device, renderer, session);
        CaptureResult fullAfterLegacy = Capture(
            device,
            renderer,
            session,
            new SilkSceneIngestionOptions(
                RenderPurpose.Default | RenderPurpose.Render,
                "full"));
        CaptureResult proxyOnly = Capture(
            device,
            renderer,
            session,
            new SilkSceneIngestionOptions(
                RenderPurpose.Default | RenderPurpose.Proxy,
                "full"));

        await Assert.That(full.DrawCount).IsGreaterThan(0);
        await Assert.That(full.DefaultMaterialPath).IsEqualTo("/World/FullMaterial");
        await Assert.That(full.PreviewMaterialKind).IsEqualTo(SilkSurfaceKind.PreviewSurface);
        await Assert.That(full.PreviewMaterialBlue).IsEqualTo(1f);
        await Assert.That(full.AllPurposeMaterialKind).IsEqualTo(SilkSurfaceKind.PreviewSurface);
        await Assert.That(full.AllPurposeMaterialRed).IsEqualTo(1f);
        await Assert.That(full.HasAllPurposeMaterial).IsTrue();
        await Assert.That(CountVisiblePixels(full.Pixels)).IsGreaterThan(0);
        await Assert.That(fullRepeat.MeshUpserts).IsEqualTo(0u);
        await Assert.That(fullRepeat.MaterialUpserts).IsEqualTo(0u);
        await Assert.That(fullRepeat.DrawCount).IsGreaterThan(0);
        await Assert.That(fullRepeat.Pixels.SequenceEqual(full.Pixels)).IsTrue();
        await Assert.That(preview.DefaultMaterialPath).IsEqualTo("/World/PreviewMaterial");
        await Assert.That(preview.RetainedMaterialPath).IsEqualTo("/World/PreviewMaterial");
        await Assert.That(preview.DrawCount).IsGreaterThan(0);
        await Assert.That(preview.Pixels.SequenceEqual(full.Pixels)).IsFalse();
        await Assert.That(CountChangedQuadrants(full.Pixels, preview.Pixels, 96, 96)).IsEqualTo(1);
        await Assert.That(fullRestore.DefaultMaterialPath).IsEqualTo("/World/FullMaterial");
        await Assert.That(fullRestore.RetainedMaterialPath).IsEqualTo("/World/FullMaterial");
        await Assert.That(fullRestore.DrawCount).IsGreaterThan(0);
        await Assert.That(fullRestore.Pixels.SequenceEqual(full.Pixels)).IsTrue();
        await Assert.That(legacy.DefaultMaterialPath).IsEqualTo("/World/AllPurposeMaterial");
        await Assert.That(legacy.RetainedMaterialPath).IsEqualTo("/World/AllPurposeMaterial");
        await Assert.That(legacy.DrawCount).IsGreaterThan(0);
        await Assert.That(legacy.MeshUpserts).IsGreaterThan(0u);
        await Assert.That(legacy.Pixels.SequenceEqual(full.Pixels)).IsFalse();
        await Assert.That(CountVisiblePixels(legacy.Pixels)).IsGreaterThan(CountVisiblePixels(full.Pixels));
        await Assert.That(fullAfterLegacy.DefaultMaterialPath).IsEqualTo("/World/FullMaterial");
        await Assert.That(fullAfterLegacy.RetainedMaterialPath).IsEqualTo("/World/FullMaterial");
        await Assert.That(fullAfterLegacy.DrawCount).IsGreaterThan(0);
        await Assert.That(fullAfterLegacy.MeshUpserts).IsGreaterThan(0u);
        await Assert.That(fullAfterLegacy.Pixels.SequenceEqual(full.Pixels)).IsTrue();
        await Assert.That(proxyOnly.DrawCount).IsGreaterThan(0);
        await Assert.That(proxyOnly.MeshRemovals).IsGreaterThanOrEqualTo(1u);
        await Assert.That(proxyOnly.Pixels.SequenceEqual(full.Pixels)).IsFalse();
        await Assert.That(CountChangedQuadrants(full.Pixels, proxyOnly.Pixels, 96, 96)).IsGreaterThanOrEqualTo(1);
        await Assert.That(File.ReadAllBytes(fixture.ScenePath).SequenceEqual(source)).IsTrue();
    }

    [Test]
    public async Task DisposedSessionRejectsExplicitSync()
    {
        using var fixture = new SceneFixture();
        OpenUsdSilkSession session = fixture.CreateSession();
        session.Dispose();

        await Assert.That(() => session.Sync(
                96,
                96,
                new SilkSceneIngestionOptions(RenderPurpose.Default, "full")))
            .Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task LegacySyncRestoresViewportDefaultsAfterExplicitProductFilters()
    {
        using var fixture = new SceneFixture();
        using OpenUsdSilkSession session = fixture.CreateSession();
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(SilkGraphicsBackend.D3D12);
        using var renderer = new SilkMeshRenderer(device);

        _ = Capture(
            device,
            renderer,
            session,
            new SilkSceneIngestionOptions(
                RenderPurpose.Default | RenderPurpose.Render,
                "full"));
        CaptureResult legacy = CaptureLegacy(device, renderer, session);

        await Assert.That(legacy.DefaultMaterialPath).IsEqualTo("/World/AllPurposeMaterial");
        await Assert.That(legacy.RetainedMaterialPath).IsEqualTo("/World/AllPurposeMaterial");
        await Assert.That(legacy.DrawCount).IsGreaterThan(0);
        await Assert.That(CountVisiblePixels(legacy.Pixels)).IsGreaterThan(0);
    }

    [Test]
    public async Task ExplicitConstructorRejectsEmptyAllPurposeBinding()
    {
        await Assert.That(() => new SilkSceneIngestionOptions(
                RenderPurpose.Default | RenderPurpose.Render,
                string.Empty))
            .Throws<NotSupportedException>();
        await Assert.That(() => new SilkSceneIngestionOptions(
                RenderPurpose.Default | RenderPurpose.Render,
                "allPurpose"))
            .Throws<NotSupportedException>();
    }

    [Test]
    public async Task ExplicitConstructorRejectsUnsupportedCustomBindingPurpose()
    {
        await Assert.That(() => new SilkSceneIngestionOptions(
                RenderPurpose.Default | RenderPurpose.Render,
                "customLook"))
            .Throws<NotSupportedException>();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task TexturedPreviewSurfaceChangesPixelsAndRoundTripsExactly(SilkGraphicsBackend backend)
    {
        using var fixture = new TexturedSceneFixture();
        using OpenUsdSilkSession session = fixture.CreateSession();
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var renderer = new SilkMeshRenderer(device);

        CaptureResult full = Capture(
            device,
            renderer,
            session,
            new SilkSceneIngestionOptions(
                RenderPurpose.Default | RenderPurpose.Render,
                "full"));
        CaptureResult preview = Capture(
            device,
            renderer,
            session,
            new SilkSceneIngestionOptions(
                RenderPurpose.Default | RenderPurpose.Render,
                "preview"));
        CaptureResult fullRestore = Capture(
            device,
            renderer,
            session,
            new SilkSceneIngestionOptions(
                RenderPurpose.Default | RenderPurpose.Render,
                "full"));

        await Assert.That(full.DrawCount).IsGreaterThan(0);
        await Assert.That(preview.DrawCount).IsGreaterThan(0);
        await Assert.That(full.DefaultMaterialPath).IsEqualTo("/World/FullMaterial");
        await Assert.That(preview.DefaultMaterialPath).IsEqualTo("/World/PreviewMaterial");
        await Assert.That(preview.Pixels.SequenceEqual(full.Pixels)).IsFalse();
        await Assert.That(CountChangedQuadrants(full.Pixels, preview.Pixels, 96, 96)).IsEqualTo(1);
        await Assert.That(fullRestore.Pixels.SequenceEqual(full.Pixels)).IsTrue();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task SharedHdrCapturerRestoresLegacyQuadrantsAndProductRoundTrip(SilkGraphicsBackend backend)
    {
        using var fixture = new ProductLikeSceneFixture();
        using OpenUsdSilkSession session = fixture.CreateSession();
        using OpenUsd.UsdStage stage = OpenUsd.UsdStage.Open(fixture.ScenePath);
        CameraState camera = CameraState.FromStageCamera(stage, "/Camera", 0, 16, 16);
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var capturer = new SilkFrameCapturer(device);

        SilkFrameCaptureResult full = capturer.CaptureWithHdrColor(
            session,
            16,
            16,
            RenderSettings.PresentationDefault,
            new SilkSceneIngestionOptions(RenderPurpose.Default | RenderPurpose.Render, "full"),
            new SilkHdrColorCaptureOptions(),
            camera: camera);
        SilkFrameCaptureResult preview = capturer.CaptureWithHdrColor(
            session,
            16,
            16,
            RenderSettings.PresentationDefault,
            new SilkSceneIngestionOptions(RenderPurpose.Default | RenderPurpose.Proxy, "preview"),
            new SilkHdrColorCaptureOptions(),
            camera: camera);
        SilkFrameCaptureResult legacy = capturer.CaptureWithHdrColor(
            session,
            16,
            16,
            RenderSettings.PresentationDefault,
            new SilkHdrColorCaptureOptions(),
            camera: camera);
        SilkFrameCaptureResult restored = capturer.CaptureWithHdrColor(
            session,
            16,
            16,
            RenderSettings.PresentationDefault,
            new SilkSceneIngestionOptions(RenderPurpose.Default | RenderPurpose.Render, "full"),
            new SilkHdrColorCaptureOptions(),
            camera: camera);

        byte[] fullPixels = full.HdrColor!.Rgba16Float.ToArray();
        byte[] previewPixels = preview.HdrColor!.Rgba16Float.ToArray();
        byte[] legacyPixels = legacy.HdrColor!.Rgba16Float.ToArray();
        byte[] restoredPixels = restored.HdrColor!.Rgba16Float.ToArray();

        await AssertHalf(fullPixels, 4, 4, "005400000000003C");
        await AssertHalf(fullPixels, 12, 4, "005400000000003C");
        await AssertHalf(fullPixels, 4, 12, "000000000000003C");
        await AssertHalf(fullPixels, 12, 12, "000000000000003C");

        await AssertHalf(previewPixels, 4, 4, "000000540000003C");
        await AssertHalf(previewPixels, 12, 4, "000000000000003C");
        await AssertHalf(previewPixels, 4, 12, "000000540000003C");
        await AssertHalf(previewPixels, 12, 12, "000000000000003C");

        await AssertHalf(legacyPixels, 4, 4, "000000000054003C");
        await AssertHalf(legacyPixels, 12, 4, "000000000054003C");
        await AssertHalf(legacyPixels, 4, 12, "000000000054003C");
        await AssertHalf(legacyPixels, 12, 12, "000000000000003C");

        await Assert.That(restoredPixels.SequenceEqual(fullPixels)).IsTrue();
        await Assert.That(File.ReadAllBytes(fixture.ScenePath).SequenceEqual(fixture.SourceBytes)).IsTrue();
    }

    private static CaptureResult Capture(
        ISilkGraphicsDevice device,
        SilkMeshRenderer renderer,
        OpenUsdSilkSession session,
        SilkSceneIngestionOptions options)
    {
        using OpenUsdSilkPage page = session.Sync(
            96,
            96,
            options,
            camera: SceneFixture.Camera);
        PageInfo pageInfo = ReadPageInfo(page);
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            new SilkTextureDescriptor(
                96,
                96,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(96, 96));
        SilkMeshRenderResult render = renderer.ApplyAndRender(
            page,
            color,
            depth,
            new SilkMeshRenderOptions(new SilkColor(0, 0, 0, 1), 1));

        byte[] pixels = new byte[96 * 96 * 4];
        color.ReadbackForTesting(pixels);
        string retainedMaterialPath = renderer.Scene.MeshesByPath[("/World/DefaultQuad", 0)].MaterialPath;
        bool hasAllPurposeMaterial = renderer.Scene.Materials.ContainsKey("/World/AllPurposeMaterial");
        float? retainedAllPurposeMaterialRed = null;
        if (hasAllPurposeMaterial)
        {
            SilkMaterialScalar? emissive =
                renderer.Scene.Materials["/World/AllPurposeMaterial"].Scalars
                    .FirstOrDefault(static scalar => scalar.Parameter == SilkMaterialParameter.EmissiveColor);
            if (emissive is not null)
            {
                retainedAllPurposeMaterialRed = emissive.Values[0];
            }
        }
        return new CaptureResult(
            pixels,
            SampleQuadrant(pixels, 96, 96, left: true, top: true),
            SampleQuadrant(pixels, 96, 96, left: false, top: true),
            SampleQuadrant(pixels, 96, 96, left: true, top: false),
            SampleQuadrant(pixels, 96, 96, left: false, top: false),
            pageInfo.DefaultMaterialPath,
            pageInfo.PreviewMaterialKind,
            pageInfo.PreviewMaterialBlue,
            pageInfo.PreviewTextureCount,
            pageInfo.AllPurposeMaterialKind,
            pageInfo.AllPurposeMaterialRed,
            retainedMaterialPath,
            hasAllPurposeMaterial,
            retainedAllPurposeMaterialRed,
            render.DrawCount,
            Count(page, SilkCommandType.MeshUpsert),
            Count(page, SilkCommandType.MeshRemove),
            Count(page, SilkCommandType.MaterialUpsert),
            Count(page, SilkCommandType.MaterialRemove));
    }

    private static CaptureResult CaptureLegacy(
        ISilkGraphicsDevice device,
        SilkMeshRenderer renderer,
        OpenUsdSilkSession session)
    {
        using OpenUsdSilkPage page = session.Sync(96, 96, camera: SceneFixture.Camera);
        PageInfo pageInfo = ReadPageInfo(page);
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            new SilkTextureDescriptor(
                96,
                96,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(96, 96));
        SilkMeshRenderResult render = renderer.ApplyAndRender(
            page,
            color,
            depth,
            new SilkMeshRenderOptions(new SilkColor(0, 0, 0, 1), 1));

        byte[] pixels = new byte[96 * 96 * 4];
        color.ReadbackForTesting(pixels);
        string retainedMaterialPath = renderer.Scene.MeshesByPath[("/World/DefaultQuad", 0)].MaterialPath;
        bool hasAllPurposeMaterial = renderer.Scene.Materials.ContainsKey("/World/AllPurposeMaterial");
        float? retainedAllPurposeMaterialRed = null;
        if (hasAllPurposeMaterial)
        {
            SilkMaterialScalar? emissive =
                renderer.Scene.Materials["/World/AllPurposeMaterial"].Scalars
                    .FirstOrDefault(static scalar => scalar.Parameter == SilkMaterialParameter.EmissiveColor);
            if (emissive is not null)
            {
                retainedAllPurposeMaterialRed = emissive.Values[0];
            }
        }
        return new CaptureResult(
            pixels,
            SampleQuadrant(pixels, 96, 96, left: true, top: true),
            SampleQuadrant(pixels, 96, 96, left: false, top: true),
            SampleQuadrant(pixels, 96, 96, left: true, top: false),
            SampleQuadrant(pixels, 96, 96, left: false, top: false),
            pageInfo.DefaultMaterialPath,
            pageInfo.PreviewMaterialKind,
            pageInfo.PreviewMaterialBlue,
            pageInfo.PreviewTextureCount,
            pageInfo.AllPurposeMaterialKind,
            pageInfo.AllPurposeMaterialRed,
            retainedMaterialPath,
            hasAllPurposeMaterial,
            retainedAllPurposeMaterialRed,
            render.DrawCount,
            Count(page, SilkCommandType.MeshUpsert),
            Count(page, SilkCommandType.MeshRemove),
            Count(page, SilkCommandType.MaterialUpsert),
            Count(page, SilkCommandType.MaterialRemove));
    }

    private static uint Count(OpenUsdSilkPage page, SilkCommandType type)
    {
        uint count = 0;
        using SilkCommandEnumerator commands = page.GetEnumerator();
        while (commands.MoveNext())
        {
            if (commands.Current.Type == type)
            {
                count++;
            }
        }
        return count;
    }

    private static SampledColor SampleQuadrant(byte[] pixels, int width, int height, bool left, bool top)
    {
        int startX = left ? 0 : width / 2;
        int endX = left ? width / 2 : width;
        int startY = top ? height / 2 : 0;
        int endY = top ? height : height / 2;
        float red = 0;
        float green = 0;
        float blue = 0;
        int hits = 0;
        for (int y = startY; y < endY; y++)
        {
            for (int x = startX; x < endX; x++)
            {
                int offset = ((y * width) + x) * 4;
                byte r = pixels[offset];
                byte g = pixels[offset + 1];
                byte b = pixels[offset + 2];
                if (r == 0 && g == 0 && b == 0)
                {
                    continue;
                }
                red += r / 255f;
                green += g / 255f;
                blue += b / 255f;
                hits++;
            }
        }

        return hits == 0
            ? new SampledColor(0, 0, 0)
            : new SampledColor(red / hits, green / hits, blue / hits);
    }

    private static int CountVisiblePixels(byte[] pixels)
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

    private static int CountChangedQuadrants(byte[] left, byte[] right, int width, int height)
    {
        float[] diffs = new float[4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int quadrant = (x < width / 2 ? 0 : 1) + (y < height / 2 ? 0 : 2);
                int offset = ((y * width) + x) * 4;
                diffs[quadrant] +=
                    Math.Abs(left[offset] - right[offset]) +
                    Math.Abs(left[offset + 1] - right[offset + 1]) +
                    Math.Abs(left[offset + 2] - right[offset + 2]);
            }
        }

        return diffs.Count(static diff => diff > 1024f);
    }

    private static async Task AssertHalf(byte[] pixels, int x, int y, string expected) =>
        await Assert.That(Convert.ToHexString(pixels.AsSpan((y * 16 + x) * 8, 8))).IsEqualTo(expected);

    private static PageInfo ReadPageInfo(OpenUsdSilkPage page)
    {
        string defaultMaterialPath = string.Empty;
        SilkSurfaceKind? previewMaterialKind = null;
        float? previewMaterialBlue = null;
        int previewTextureCount = 0;
        SilkSurfaceKind? allPurposeMaterialKind = null;
        float? allPurposeMaterialRed = null;
        using SilkCommandEnumerator commands = page.GetEnumerator();
        while (commands.MoveNext())
        {
            if (commands.Current.Type == SilkCommandType.MeshUpsert)
            {
                SilkMeshUpsertCommand mesh = commands.Current.AsMeshUpsert();
                if (mesh.Path == "/World/DefaultQuad")
                {
                    defaultMaterialPath = mesh.MaterialPath;
                }
            }
            else if (commands.Current.Type == SilkCommandType.MaterialUpsert)
            {
                SilkMaterialUpsertCommand material = commands.Current.AsMaterialUpsert();
                if (material.Path == "/World/PreviewMaterial")
                {
                    previewMaterialKind = material.SurfaceKind;
                    previewTextureCount = material.TextureCount;
                    for (int index = 0; index < material.ScalarCount; index++)
                    {
                        SilkMaterialScalarEntry scalar = material.GetScalar(index);
                        if (scalar.Parameter == SilkMaterialParameter.EmissiveColor)
                        {
                            previewMaterialBlue = scalar.GetComponent(2);
                        }
                    }
                }
                else if (material.Path == "/World/AllPurposeMaterial")
                {
                    allPurposeMaterialKind = material.SurfaceKind;
                    for (int index = 0; index < material.ScalarCount; index++)
                    {
                        SilkMaterialScalarEntry scalar = material.GetScalar(index);
                        if (scalar.Parameter == SilkMaterialParameter.EmissiveColor)
                        {
                            allPurposeMaterialRed = scalar.GetComponent(0);
                        }
                    }
                }
            }
        }

        return new PageInfo(
            defaultMaterialPath,
            previewMaterialKind,
            previewMaterialBlue,
            previewTextureCount,
            allPurposeMaterialKind,
            allPurposeMaterialRed);
    }

    private readonly record struct SampledColor(float Red, float Green, float Blue)
    {
        public float Intensity => Red + Green + Blue;
    }

    private readonly record struct PageInfo(
        string DefaultMaterialPath,
        SilkSurfaceKind? PreviewMaterialKind,
        float? PreviewMaterialBlue,
        int PreviewTextureCount,
        SilkSurfaceKind? AllPurposeMaterialKind,
        float? AllPurposeMaterialRed);

    private sealed record CaptureResult(
        byte[] Pixels,
        SampledColor TopLeft,
        SampledColor TopRight,
        SampledColor BottomLeft,
        SampledColor BottomRight,
        string DefaultMaterialPath,
        SilkSurfaceKind? PreviewMaterialKind,
        float? PreviewMaterialBlue,
        int PreviewTextureCount,
        SilkSurfaceKind? AllPurposeMaterialKind,
        float? AllPurposeMaterialRed,
        string RetainedMaterialPath,
        bool HasAllPurposeMaterial,
        float? RetainedAllPurposeMaterialRed,
        int DrawCount,
        uint MeshUpserts,
        uint MeshRemovals,
        uint MaterialUpserts,
        uint MaterialRemovals);

    private sealed class SceneFixture : IDisposable
    {
        internal SceneFixture()
        {
            Root = Path.Combine(
                Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT") ??
                Path.Combine(AppContext.BaseDirectory, "TestResults"),
                "scene-ingestion-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            ScenePath = Path.Combine(Root, "scene.usda");
            File.WriteAllText(ScenePath, Scene, new UTF8Encoding(false));
        }

        internal string Root { get; }

        internal string ScenePath { get; }

        internal static CameraState Camera { get; } = new(
            Matrix4x4.CreateTranslation(0, 0, -5),
            new Matrix4x4(
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, -0.2f, 0,
                0, 0, -1.2f, 1));

        internal OpenUsdSilkSession CreateSession()
        {
            string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
            if (string.IsNullOrWhiteSpace(plugins))
            {
                Skip.Test("Set OPENUSD_TEST_PLUGIN_PATH to the matched native runtime plugin directory.");
                throw new InvalidOperationException("Skip.Test returned unexpectedly.");
            }
            return OpenUsdSilkRuntime.Create(plugins, ScenePath);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private const string Scene = """
            #usda 1.0
            (
                defaultPrim = "World"
                renderSettingsPrimPath = "/Settings"
                metersPerUnit = 1
                upAxis = "Y"
            )
            def Xform "World"
            {
                def Camera "Camera"
                {
                    token projection = "orthographic"
                    float horizontalAperture = 20
                    float verticalAperture = 20
                    float focalLength = 10
                    float2 clippingRange = (1, 11)
                    double3 xformOp:translate = (0, 0, 5)
                    uniform token[] xformOpOrder = ["xformOp:translate"]
                }

                def Mesh "DefaultQuad" (
                    prepend apiSchemas = ["MaterialBindingAPI"]
                )
                {
                    uniform token subdivisionScheme = "none"
                    uniform bool doubleSided = 1
                    point3f[] points = [(-0.8, 0.2, 0), (-0.2, 0.2, 0), (-0.2, 0.8, 0), (-0.8, 0.8, 0)]
                    int[] faceVertexCounts = [4]
                    int[] faceVertexIndices = [0, 1, 2, 3]
                    rel material:binding = </World/AllPurposeMaterial>
                    rel material:binding:full = </World/FullMaterial>
                    rel material:binding:preview = </World/PreviewMaterial>
                    rel material:binding:customLook = </World/CustomMaterial>
                }

                def Xform "ProxyParent"
                {
                    uniform token purpose = "proxy"
                    def Mesh "InheritedProxyQuad" (
                        prepend apiSchemas = ["MaterialBindingAPI"]
                    )
                    {
                        uniform token subdivisionScheme = "none"
                        uniform bool doubleSided = 1
                        point3f[] points = [(-0.8, -0.8, 0), (-0.2, -0.8, 0), (-0.2, -0.2, 0), (-0.8, -0.2, 0)]
                        int[] faceVertexCounts = [4]
                        int[] faceVertexIndices = [0, 1, 2, 3]
                        rel material:binding = </World/ProxyMaterial>
                    }
                }

                def Mesh "RenderQuad" (
                    prepend apiSchemas = ["MaterialBindingAPI"]
                )
                {
                    uniform token purpose = "render"
                    uniform token subdivisionScheme = "none"
                    uniform bool doubleSided = 1
                    point3f[] points = [(0.2, 0.2, 0), (0.8, 0.2, 0), (0.8, 0.8, 0), (0.2, 0.8, 0)]
                    int[] faceVertexCounts = [4]
                    int[] faceVertexIndices = [0, 1, 2, 3]
                    rel material:binding = </World/RenderMaterial>
                }

                def Mesh "GuideQuad" (
                    prepend apiSchemas = ["MaterialBindingAPI"]
                )
                {
                    uniform token purpose = "guide"
                    uniform token subdivisionScheme = "none"
                    uniform bool doubleSided = 1
                    point3f[] points = [(0.2, -0.8, 0), (0.8, -0.8, 0), (0.8, -0.2, 0), (0.2, -0.2, 0)]
                    int[] faceVertexCounts = [4]
                    int[] faceVertexIndices = [0, 1, 2, 3]
                    rel material:binding = </World/GuideMaterial>
                }

                def Material "AllPurposeMaterial"
                {
                    token outputs:surface.connect = </World/AllPurposeMaterial/Surface.outputs:surface>
                    def Shader "Surface"
                    {
                        uniform token info:id = "UsdPreviewSurface"
                        color3f inputs:diffuseColor = (0, 0, 0)
                        color3f inputs:emissiveColor = (1, 0, 0)
                        float inputs:opacity = 1
                        token outputs:surface
                    }
                }

                def Material "FullMaterial"
                {
                    token outputs:surface.connect = </World/FullMaterial/Surface.outputs:surface>
                    def Shader "Surface"
                    {
                        uniform token info:id = "UsdPreviewSurface"
                        color3f inputs:diffuseColor = (0, 0, 0)
                        color3f inputs:emissiveColor = (0, 1, 0)
                        float inputs:opacity = 1
                        token outputs:surface
                    }
                }

                def Material "PreviewMaterial"
                {
                    token outputs:surface.connect = </World/PreviewMaterial/Surface.outputs:surface>
                    def Shader "Surface"
                    {
                        uniform token info:id = "UsdPreviewSurface"
                        color3f inputs:diffuseColor = (0, 0, 0)
                        color3f inputs:emissiveColor = (0, 0, 1)
                        float inputs:opacity = 1
                        token outputs:surface
                    }
                }

                def Material "CustomMaterial"
                {
                    token outputs:surface.connect = </World/CustomMaterial/Surface.outputs:surface>
                    def Shader "Surface"
                    {
                        uniform token info:id = "UsdPreviewSurface"
                        color3f inputs:diffuseColor = (0, 0, 0)
                        color3f inputs:emissiveColor = (1, 1, 0)
                        float inputs:opacity = 1
                        token outputs:surface
                    }
                }

                def Material "ProxyMaterial"
                {
                    token outputs:surface.connect = </World/ProxyMaterial/Surface.outputs:surface>
                    def Shader "Surface"
                    {
                        uniform token info:id = "UsdPreviewSurface"
                        color3f inputs:diffuseColor = (0, 0, 0)
                        color3f inputs:emissiveColor = (0, 1, 1)
                        float inputs:opacity = 1
                        token outputs:surface
                    }
                }

                def Material "RenderMaterial"
                {
                    token outputs:surface.connect = </World/RenderMaterial/Surface.outputs:surface>
                    def Shader "Surface"
                    {
                        uniform token info:id = "UsdPreviewSurface"
                        color3f inputs:diffuseColor = (0, 0, 0)
                        color3f inputs:emissiveColor = (1, 0, 1)
                        float inputs:opacity = 1
                        token outputs:surface
                    }
                }

                def Material "GuideMaterial"
                {
                    token outputs:surface.connect = </World/GuideMaterial/Surface.outputs:surface>
                    def Shader "Surface"
                    {
                        uniform token info:id = "UsdPreviewSurface"
                        color3f inputs:diffuseColor = (0, 0, 0)
                        color3f inputs:emissiveColor = (1, 1, 1)
                        float inputs:opacity = 1
                        token outputs:surface
                    }
                }
            }
            def RenderSettings "Settings"
            {
                rel camera = </World/Camera>
                rel products = </Product>
                int2 resolution = (96, 96)
            }
            def RenderProduct "Product"
            {
                token productName = "never-created.exr"
                rel orderedVars = </Color>
            }
            def RenderVar "Color"
            {
                token dataType = "color3f"
                string sourceName = "color"
                token sourceType = "raw"
            }
            """;
    }

    private sealed class TexturedSceneFixture : IDisposable
    {
        internal TexturedSceneFixture()
        {
            Root = Path.Combine(
                Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT") ??
                Path.Combine(AppContext.BaseDirectory, "TestResults"),
                "scene-ingestion-textured-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            TexturePath = Path.Combine(Root, "preview.png");
            ScenePath = Path.Combine(Root, "scene.usda");

            byte[] png = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9p0Pfr8AAAAASUVORK5CYII=");
            File.WriteAllBytes(TexturePath, png);
            File.WriteAllText(
                ScenePath,
                Scene.Replace("__TEXTURE__", TexturePath.Replace("\\", "/"), StringComparison.Ordinal),
                new UTF8Encoding(false));
        }

        internal string Root { get; }

        internal string TexturePath { get; }

        internal string ScenePath { get; }

        internal OpenUsdSilkSession CreateSession()
        {
            string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
            if (string.IsNullOrWhiteSpace(plugins))
            {
                Skip.Test("Set OPENUSD_TEST_PLUGIN_PATH to the matched native runtime plugin directory.");
                throw new InvalidOperationException("Skip.Test returned unexpectedly.");
            }
            return OpenUsdSilkRuntime.Create(plugins, ScenePath);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private const string Scene = """
            #usda 1.0
            (
                defaultPrim = "World"
                renderSettingsPrimPath = "/Settings"
                metersPerUnit = 1
                upAxis = "Y"
            )
            def Xform "World"
            {
                def Camera "Camera"
                {
                    token projection = "orthographic"
                    float horizontalAperture = 20
                    float verticalAperture = 20
                    float focalLength = 10
                    float2 clippingRange = (1, 11)
                    double3 xformOp:translate = (0, 0, 5)
                    uniform token[] xformOpOrder = ["xformOp:translate"]
                }

                def Mesh "DefaultQuad" (
                    prepend apiSchemas = ["MaterialBindingAPI"]
                )
                {
                    uniform token subdivisionScheme = "none"
                    uniform bool doubleSided = 1
                    point3f[] points = [(-0.8, 0.2, 0), (-0.2, 0.2, 0), (-0.2, 0.8, 0), (-0.8, 0.8, 0)]
                    int[] faceVertexCounts = [4]
                    int[] faceVertexIndices = [0, 1, 2, 3]
                    rel material:binding = </World/AllPurposeMaterial>
                    rel material:binding:full = </World/FullMaterial>
                    rel material:binding:preview = </World/PreviewMaterial>
                }

                def Mesh "RenderQuad" (
                    prepend apiSchemas = ["MaterialBindingAPI"]
                )
                {
                    uniform token purpose = "render"
                    uniform token subdivisionScheme = "none"
                    uniform bool doubleSided = 1
                    point3f[] points = [(0.2, 0.2, 0), (0.8, 0.2, 0), (0.8, 0.8, 0), (0.2, 0.8, 0)]
                    int[] faceVertexCounts = [4]
                    int[] faceVertexIndices = [0, 1, 2, 3]
                    rel material:binding = </World/FullMaterial>
                }

                def Material "AllPurposeMaterial"
                {
                    token outputs:surface.connect = </World/AllPurposeMaterial/Surface.outputs:surface>
                    def Shader "Surface"
                    {
                        uniform token info:id = "UsdPreviewSurface"
                        color3f inputs:diffuseColor = (1, 0, 0)
                        float inputs:opacity = 1
                        token outputs:surface
                    }
                }

                def Material "FullMaterial"
                {
                    token outputs:surface.connect = </World/FullMaterial/Surface.outputs:surface>
                    def Shader "Surface"
                    {
                        uniform token info:id = "UsdPreviewSurface"
                        color3f inputs:diffuseColor = (0, 1, 0)
                        float inputs:opacity = 1
                        token outputs:surface
                    }
                }

                def Material "PreviewMaterial"
                {
                    token outputs:surface.connect = </World/PreviewMaterial/Surface.outputs:surface>
                    def Shader "Surface"
                    {
                        uniform token info:id = "UsdPreviewSurface"
                        color3f inputs:diffuseColor = (0, 0, 0)
                        color3f inputs:emissiveColor.connect = </World/PreviewMaterial/Tex.outputs:rgb>
                        float inputs:opacity = 1
                        token outputs:surface
                    }
                    def Shader "Tex"
                    {
                        uniform token info:id = "UsdUVTexture"
                        asset inputs:file = @__TEXTURE__@
                        token inputs:sourceColorSpace = "sRGB"
                        float2 inputs:st.connect = </World/PreviewMaterial/StReader.outputs:result>
                        token outputs:rgb
                    }
                    def Shader "StReader"
                    {
                        uniform token info:id = "UsdPrimvarReader_float2"
                        token inputs:varname = "st"
                        float2 outputs:result
                    }
                }
            }
            def RenderSettings "Settings"
            {
                rel camera = </World/Camera>
                rel products = </Product>
                int2 resolution = (96, 96)
            }
            def RenderProduct "Product"
            {
                token productName = "never-created.exr"
                rel orderedVars = </Color>
            }
            def RenderVar "Color"
            {
                token dataType = "color3f"
                string sourceName = "color"
                token sourceType = "raw"
            }
            """;
    }

    private sealed class ProductLikeSceneFixture : IDisposable
    {
        internal ProductLikeSceneFixture()
        {
            Root = Path.Combine(
                Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT") ??
                Path.Combine(AppContext.BaseDirectory, "TestResults"),
                "scene-ingestion-product-like-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            ScenePath = Path.Combine(Root, "scene.usda");
            File.WriteAllText(ScenePath, Scene, new UTF8Encoding(false));
            SourceBytes = File.ReadAllBytes(ScenePath);
        }

        internal string Root { get; }

        internal string ScenePath { get; }

        internal byte[] SourceBytes { get; }

        internal OpenUsdSilkSession CreateSession()
        {
            string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
            if (string.IsNullOrWhiteSpace(plugins))
            {
                Skip.Test("Set OPENUSD_TEST_PLUGIN_PATH to the matched native runtime plugin directory.");
                throw new InvalidOperationException("Skip.Test returned unexpectedly.");
            }
            return OpenUsdSilkRuntime.Create(plugins, ScenePath);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private const string Scene = """
            #usda 1.0
            (
                renderSettingsPrimPath = "/SettingsFull"
                metersPerUnit = 1
                upAxis = "Y"
            )
            def Camera "Camera" {
                token projection = "orthographic"
                float horizontalAperture = 40
                float verticalAperture = 40
                float2 clippingRange = (1, 11)
            }
            def Scope "Looks" {
                def Material "Full" {
                    token outputs:surface.connect = </Looks/Full/Shader.outputs:surface>
                    def Shader "Shader" {
                        uniform token info:id = "UsdPreviewSurface"
                        color3f inputs:diffuseColor = (0, 0, 0)
                        color3f inputs:emissiveColor = (64, 0, 0)
                        int inputs:useSpecularWorkflow = 1
                        color3f inputs:specularColor = (0, 0, 0)
                        token outputs:surface
                    }
                }
                def Material "Preview" {
                    token outputs:surface.connect = </Looks/Preview/Shader.outputs:surface>
                    def Shader "Shader" {
                        uniform token info:id = "UsdPreviewSurface"
                        color3f inputs:diffuseColor = (0, 0, 0)
                        color3f inputs:emissiveColor = (0, 64, 0)
                        int inputs:useSpecularWorkflow = 1
                        color3f inputs:specularColor = (0, 0, 0)
                        token outputs:surface
                    }
                }
                def Material "Legacy" {
                    token outputs:surface.connect = </Looks/Legacy/Shader.outputs:surface>
                    def Shader "Shader" {
                        uniform token info:id = "UsdPreviewSurface"
                        color3f inputs:diffuseColor = (0, 0, 0)
                        color3f inputs:emissiveColor = (0, 0, 64)
                        int inputs:useSpecularWorkflow = 1
                        color3f inputs:specularColor = (0, 0, 0)
                        token outputs:surface
                    }
                }
            }
            def Xform "World" (prepend apiSchemas = ["MaterialBindingAPI"]) {
                rel material:binding = </Looks/Legacy>
                rel material:binding:full = </Looks/Full>
                rel material:binding:preview = </Looks/Preview>
                def Cube "Default" {
                    double size = 0.75
                    double3 xformOp:translate = (-1, 1, -4)
                    uniform token[] xformOpOrder = ["xformOp:translate"]
                }
                def Cube "Render" {
                    uniform token purpose = "render"
                    double size = 0.75
                    double3 xformOp:translate = (1, 1, -4)
                    uniform token[] xformOpOrder = ["xformOp:translate"]
                }
                def Xform "ProxyParent" {
                    uniform token purpose = "proxy"
                    def Cube "InheritedProxy" {
                        double size = 0.75
                        double3 xformOp:translate = (-1, -1, -4)
                        uniform token[] xformOpOrder = ["xformOp:translate"]
                    }
                }
                def Cube "Guide" {
                    uniform token purpose = "guide"
                    double size = 0.75
                    double3 xformOp:translate = (1, -1, -4)
                    uniform token[] xformOpOrder = ["xformOp:translate"]
                }
            }
            """;
    }
}
