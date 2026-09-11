// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Numerics;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkVertexCompactionNativeTests
{
    [Test]
    [Arguments(2, false)]
    [Arguments(2, true)]
    [Arguments(128, false)]
    public async Task SharedCornersRetainEveryFaceUvAndAuthoredIdentityWithoutTriangleExpansion(int size, bool seam)
    {
        string plugins = RequirePlugins();
        string root = Directory.CreateTempSubdirectory("silk-compact-vertices-").FullName;
        string path = Path.Combine(root, "grid.usda");
        string text = Grid(size, seam);
        try
        {
            await File.WriteAllTextAsync(path, text);
            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins, path);
            Snapshot snapshot = Read(session);
            int expectedVertices = (size + 1) * (size + 1) + (seam ? size + 1 : 0);
            await Assert.That(snapshot.PointCount).IsEqualTo(expectedVertices);
            await Assert.That(snapshot.Corners.Length).IsEqualTo(size * size * 6);
            await Assert.That(snapshot.PointOrigins).IsEqualTo(snapshot.PointCount);
            for (int corner = 0; corner < snapshot.Corners.Length; corner++)
            {
                Corner value = snapshot.Corners[corner];
                int face = corner / 6;
                int x = face % size;
                int y = face / size;
                int[] quad = [y * (size + 1) + x, y * (size + 1) + x + 1,
                    (y + 1) * (size + 1) + x + 1, (y + 1) * (size + 1) + x];
                int expectedOrigin = quad[new[] { 0, 1, 2, 0, 2, 3 }[corner % 6]];
                await Assert.That(value.Face).IsEqualTo(face);
                await Assert.That(value.Origin).IsEqualTo(expectedOrigin);
                await Assert.That(value.X).IsEqualTo((float)(expectedOrigin % (size + 1)));
                await Assert.That(value.Y).IsEqualTo((float)(expectedOrigin / (size + 1)));
                await Assert.That(value.Z).IsEqualTo(0f);
                await Assert.That(value.U).IsEqualTo(value.X / size + (seam && x >= size / 2 ? 10 : 0));
                await Assert.That(value.V).IsEqualTo(value.Y / size);
            }
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task UvSeamChangesAdvanceLayoutRevisionAndRestoreExactCornerValues()
    {
        string plugins = RequirePlugins();
        string root = Directory.CreateTempSubdirectory("silk-compact-layout-").FullName;
        string path = Path.Combine(root, "grid.usda");
        string text = Grid(2, false);
        try
        {
            await File.WriteAllTextAsync(path, text);
            await using UsdStageScheduler scheduler = UsdStageScheduler.Open(path);
            using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins, source);
            Snapshot initial = Read(session);
            await scheduler.InvokeAsync(stage => stage.GetPrim("/Grid").SetVec2fArray("primvars:st", Uvs(2, true)));
            Snapshot split = Read(session);
            await scheduler.InvokeAsync(stage => stage.GetPrim("/Grid").SetVec2fArray("primvars:st", Uvs(2, false)));
            Snapshot restored = Read(session);
            await Assert.That(initial.PointCount).IsEqualTo(9);
            await Assert.That(split.PointCount).IsEqualTo(12);
            await Assert.That(restored.PointCount).IsEqualTo(9);
            await Assert.That(split.TopologyRevision).IsGreaterThan(initial.TopologyRevision);
            await Assert.That(restored.TopologyRevision).IsGreaterThan(split.TopologyRevision);
            await Assert.That(restored.Corners.SequenceEqual(initial.Corners)).IsTrue();
            await Assert.That(split.Corners.Select(corner => (corner.Face, corner.Origin, corner.Edge)))
                .IsEquivalentTo(initial.Corners.Select(corner => (corner.Face, corner.Origin, corner.Edge)));
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task MissingNormalsPreserveTriangleLocalFallback()
    {
        string plugins = RequirePlugins();
        string root = Directory.CreateTempSubdirectory("silk-local-normal-fallback-").FullName;
        string path = Path.Combine(root, "grid.usda");
        string text = Grid(2, false, normals: false);
        try
        {
            await File.WriteAllTextAsync(path, text);
            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins, path);
            Snapshot snapshot = Read(session);
            await Assert.That(snapshot.PointCount).IsEqualTo(24);
            await Assert.That(snapshot.Corners.Length).IsEqualTo(24);
            await Assert.That(snapshot.Corners.Select(corner => corner.Origin).Distinct().Count()).IsEqualTo(9);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task SharedCornerTexturePixelsMatchAnIndependentVertexIndexedScene(SilkGraphicsBackend backend)
    {
        string plugins = RequirePlugins();
        string root = Directory.CreateTempSubdirectory("silk-compact-pixels-").FullName;
        try
        {
            using (var image = File.Create(Path.Combine(root, "asymmetric.png")))
            {
                _ = PngRgba8Writer.Write(image, 2, 2,
                    new byte[] { 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 0, 255 }, 1024);
            }
            string actualPath = Path.Combine(root, "corners.usda");
            string referencePath = Path.Combine(root, "vertices.usda");
            await File.WriteAllTextAsync(actualPath, Textured(Grid(2, false)));
            await File.WriteAllTextAsync(referencePath, Textured(Grid(2, false, vertexUvs: true)));
            using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
            byte[] actual = Capture(device, plugins, actualPath);
            byte[] reference = Capture(device, plugins, referencePath);
            await Assert.That(actual.SequenceEqual(reference)).IsTrue();
            int colors = Enumerable.Range(0, actual.Length / 4)
                .Select(index => Convert.ToHexString(actual.AsSpan(index * 4, 3))).Distinct().Count();
            await Assert.That(colors).IsGreaterThan(4)
                .Because("The asymmetric UV texture must actually affect the image, not render a common fallback.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] Capture(ISilkGraphicsDevice device, string plugins, string path)
    {
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins, path);
        var camera = new CameraState(Matrix4x4.CreateTranslation(-1, -1, -5),
            new Matrix4x4(0.5f, 0, 0, 0, 0, 0.5f, 0, 0, 0, 0, -0.2f, 0, 0, 0, -1.2f, 1));
        return SilkFrameCapture.Capture(session, device, 32, 32, RenderSettings.Default, camera: camera).Rgba.ToArray();
    }

    private static string Textured(string mesh) =>
        mesh.Replace("def Mesh \"Grid\" {",
            "def Mesh \"Grid\" (prepend apiSchemas = [\"MaterialBindingAPI\"]) {\n" +
            "rel material:binding = </Material>", StringComparison.Ordinal) + """

        def Material "Material" {
            token outputs:surface.connect = </Material/Surface.outputs:surface>
            def Shader "Surface" {
                uniform token info:id = "UsdPreviewSurface"
                color3f inputs:diffuseColor.connect = </Material/Texture.outputs:rgb>
                token outputs:surface
            }
            def Shader "Texture" {
                uniform token info:id = "UsdUVTexture"
                asset inputs:file = @asymmetric.png@
                token inputs:sourceColorSpace = "raw"
                float2 inputs:st.connect = </Material/Coordinates.outputs:result>
                float3 outputs:rgb
            }
            def Shader "Coordinates" {
                uniform token info:id = "UsdPrimvarReader_float2"
                token inputs:varname = "st"
                float2 outputs:result
            }
        }
        """;

    private static Snapshot Read(OpenUsdSilkSession session)
    {
        using OpenUsdSilkPage page = session.Sync(16, 16);
        using SilkCommandEnumerator commands = page.GetEnumerator();
        while (commands.MoveNext())
        {
            if (commands.Current.Type != SilkCommandType.MeshUpsert)
            {
                continue;
            }
            SilkMeshUpsertCommand mesh = commands.Current.AsMeshUpsert();
            int uvIndex = -1;
            for (int index = 0; index < mesh.AttributeCount; index++)
            {
                if (mesh.GetAttribute(index).Name == "st")
                {
                    uvIndex = index;
                    break;
                }
            }
            if (uvIndex < 0)
            {
                throw new InvalidDataException("The native grid omitted its authored UVs.");
            }
            SilkMeshAttributeEntry uv = mesh.GetAttribute(uvIndex);
            var corners = new Corner[mesh.IndexCount];
            for (int corner = 0; corner < corners.Length; corner++)
            {
                int vertex = checked((int)mesh.GetIndex(corner));
                corners[corner] = new Corner(mesh.GetTriangleSubprim(corner / 3), mesh.GetPointOrigin(vertex),
                    mesh.GetCornerEdge(corner), mesh.GetPointComponent(vertex, 0), mesh.GetPointComponent(vertex, 1),
                    mesh.GetPointComponent(vertex, 2), uv.GetComponent(vertex, 0), uv.GetComponent(vertex, 1));
            }
            return new Snapshot(mesh.PointCount, mesh.PointOriginCount, mesh.TopologyRevision, corners);
        }
        throw new InvalidDataException("The native page omitted the expected mesh update.");
    }

    private static string Grid(int size, bool seam, bool normals = true, bool vertexUvs = false)
    {
        var text = new StringBuilder("#usda 1.0\n(defaultPrim = \"Grid\")\ndef Mesh \"Grid\" {\n");
        text.AppendLine("uniform token subdivisionScheme = \"none\"");
        if (normals)
        {
            text.AppendLine("normal3f[] normals = [(0,0,1)] (interpolation = \"constant\")");
        }
        text.Append("point3f[] points = [");
        text.AppendJoin(",", Enumerable.Range(0, (size + 1) * (size + 1))
            .Select(index => FormattableString.Invariant($"({index % (size + 1)},{index / (size + 1)},0)")));
        text.AppendLine("]");
        text.Append("int[] faceVertexCounts = [");
        text.AppendJoin(",", Enumerable.Repeat("4", size * size));
        text.AppendLine("]");
        text.Append("int[] faceVertexIndices = [");
        text.AppendJoin(",", Enumerable.Range(0, size * size).SelectMany(face =>
        {
            int first = (face / size) * (size + 1) + face % size;
            return new[] { first, first + 1, first + size + 2, first + size + 1 };
        }));
        text.AppendLine("]");
        text.Append("texCoord2f[] primvars:st = [");
        UsdVec2f[] uvs = vertexUvs
            ? Enumerable.Range(0, (size + 1) * (size + 1))
                .Select(index => new UsdVec2f((float)(index % (size + 1)) / size,
                    (float)(index / (size + 1)) / size)).ToArray()
            : Uvs(size, seam);
        text.AppendJoin(",", uvs.Select(value =>
            string.Create(CultureInfo.InvariantCulture, $"({value.X},{value.Y})")));
        text.AppendLine(vertexUvs ? "] (interpolation = \"vertex\")" : "] (interpolation = \"faceVarying\")");
        text.AppendLine("}");
        return text.ToString();
    }

    private static UsdVec2f[] Uvs(int size, bool seam) =>
        Enumerable.Range(0, size * size).SelectMany(face =>
        {
            int x = face % size;
            int y = face / size;
            float offset = seam && x >= size / 2 ? 10 : 0;
            return new[]
            {
                new UsdVec2f((float)x / size + offset, (float)y / size),
                new UsdVec2f((float)(x + 1) / size + offset, (float)y / size),
                new UsdVec2f((float)(x + 1) / size + offset, (float)(y + 1) / size),
                new UsdVec2f((float)x / size + offset, (float)(y + 1) / size)
            };
        }).ToArray();

    private static string RequirePlugins()
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_VERTEX_COMPACTION_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_VERTEX_COMPACTION_REQUIRED=1 with a matched native runtime.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        return Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH") ??
            throw new InvalidOperationException("A matched native plugin directory is required.");
    }

    private sealed record Snapshot(int PointCount, int PointOrigins, ulong TopologyRevision, Corner[] Corners);
    private readonly record struct Corner(int Face, int Origin, int Edge, float X, float Y, float Z, float U, float V);
}
