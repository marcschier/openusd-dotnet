// Copyright (c) marcschier. Licensed under the MIT License.

using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Mcp.Tests;

public sealed partial class NativeMcpIntegrationTests
{
    [Test]
    [NotInParallel]
    public async Task OfficialProtocolEditsNativePixelsRollsBackAndRetainsOriginalArtifactsAfterClose()
    {
        NativeLayout layout = RequireNativeLayout(requireImaging: true);
        using var files = new WorkspaceTestFiles();
        const string scene = """
            #usda 1.0
            (metersPerUnit = 1)
            def Camera "Camera" {
                token projection = "orthographic"
                float horizontalAperture = 20
                float verticalAperture = 20
                float2 clippingRange = (1, 10)
            }
            def Mesh "Quad" (prepend apiSchemas = ["MaterialBindingAPI"]) {
                uniform token subdivisionScheme = "none"
                uniform bool doubleSided = true
                point3f[] points = [(-0.5,-0.5,-4), (0.5,-0.5,-4), (0.5,0.5,-4), (-0.5,0.5,-4)]
                int[] faceVertexCounts = [4]
                int[] faceVertexIndices = [0,1,2,3]
                rel material:binding = </Material>
            }
            def Material "Material" {
                token outputs:surface.connect = </Material/Shader.outputs:surface>
                def Shader "Shader" {
                    uniform token info:id = "UsdPreviewSurface"
                    color3f inputs:diffuseColor = (0,0,0)
                    color3f inputs:emissiveColor = (64,0,0)
                    int inputs:useSpecularWorkflow = 1
                    color3f inputs:specularColor = (0,0,0)
                    token outputs:surface
                }
            }
            """;
        await File.WriteAllTextAsync(files.SourcePath, scene);
        var gpuBudget = new SilkGpuBufferBudget(1_000_000);
        var textureBudgets = new SilkTexturePreparationBudgets(new(1_000_000), new(1_000_000), new(1_000_000));
        var options = new OpenUsdMcpApplicationOptions(files.SourceRoot, files.OutputRoot,
            Path.Combine(layout.ShimRoot, "plugin", "usd"), files.OutputRoot,
            Path.Combine(files.OutputRoot, "viewer-not-launched.exe"),
            PreparationLimits: new SilkPreparationLimits(1_000_000, 1_000_000),
            GpuBufferBudget: gpuBudget, TexturePreparationBudgets: textureBudgets);
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOpenUsdMcpServices(options);
        services.AddMcpServer().WithStreamServerTransport(
            clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream()).WithOpenUsdTools();
        await using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        McpServer server = provider.GetRequiredService<McpServer>();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        Task serverTask = server.RunAsync(lifetime.Token);
        try
        {
            await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(
                clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()), cancellationToken: lifetime.Token);
            JsonElement opened = await invoke("open_scene", new() { ["sourcePath"] = "scene.usda" });
            JsonElement checkpoint = await invoke("checkpoint_scene", revision(opened));
            (string originalUri, byte[] originalPixels) = await capture(opened);
            await assertCenter(originalPixels, red: true);
            var analysisRequest = revision(opened);
            analysisRequest["observations"] = new AnalysisObservationsDto
            {
                RendererId = "silk",
                SubjectCoverage = 0.1,
                SamplesPerPixel = 1,
                ShadowsEnabled = false
            };
            JsonElement analysis = await invoke("analyze_scene", analysisRequest);
            JsonElement framing = analysis.GetProperty("proposals").EnumerateArray()
                .Single(static proposal => proposal.GetProperty("code").GetString() == "camera.framing");
            await Assert.That(framing.GetProperty("operation").GetString()).IsEqualTo("inspect-camera-framing");
            await Assert.That(framing.GetProperty("arguments")[0].GetProperty("value").GetString()).IsEqualTo("0.65");
            await Assert.That(framing.GetProperty("expectedMetrics")[0].GetProperty("current").GetDouble())
                .IsEqualTo(0.1);
            await Assert.That(framing.GetProperty("expectedMetrics")[0].GetProperty("expected").GetDouble())
                .IsEqualTo(0.65);
            JsonElement quality = analysis.GetProperty("proposals").EnumerateArray()
                .Single(static proposal => proposal.GetProperty("code").GetString() == "render.quality-alternative");
            await Assert.That(quality.GetProperty("advisory").GetBoolean()).IsTrue();
            await Assert.That(quality.GetProperty("rendererId").GetString()).IsEqualTo("silk");
            await Assert.That(quality.GetProperty("evidence").EnumerateArray()
                .Single(static item => item.GetProperty("name").GetString() == "currentSamplesPerPixel")
                .GetProperty("value").GetString()).IsEqualTo("1");
            var diagnosticApply = revision(opened);
            diagnosticApply["proposalIds"] = new[] { framing.GetProperty("id").GetString()! };
            CallToolResult diagnosticRefusal = await client.CallToolAsync("apply_proposals",
                new Dictionary<string, object?> { ["request"] = diagnosticApply },
                cancellationToken: lifetime.Token);
            await Assert.That(diagnosticRefusal.IsError).IsTrue();
            await Assert.That(diagnosticRefusal.StructuredContent!.Value.GetProperty("error")
                .GetProperty("code").GetString()).IsEqualTo("proposal_stale");
            (_, byte[] analyzedPixels) = await capture(opened);
            await Assert.That(analyzedPixels.SequenceEqual(originalPixels)).IsTrue();
            var edits = revision(opened);
            edits["edits"] = new[]
            {
                new WorkspaceEditDto
                {
                    Kind = "set_color3f", PrimPath = "/Material/Shader",
                    AttributeName = "inputs:emissiveColor", VectorValue = [0, 64, 0]
                }
            };
            JsonElement edited = await invoke("apply_edits", edits);
            await Assert.That(edited.GetProperty("generation").GetInt64())
                .IsEqualTo(opened.GetProperty("generation").GetInt64() + 1);
            await Assert.That(edited.GetProperty("stageRevision").GetUInt64())
                .IsGreaterThan(opened.GetProperty("stageRevision").GetUInt64());
            CallToolResult stale = await client.CallToolAsync("render_preview",
                new Dictionary<string, object?> { ["request"] = previewRequest(opened) },
                cancellationToken: lifetime.Token);
            await Assert.That(stale.IsError).IsTrue();
            await Assert.That(stale.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString())
                .IsEqualTo("stale_revision");
            (string editedUri, byte[] editedPixels) = await capture(edited);
            await assertCenter(editedPixels, red: false);
            await Assert.That(editedPixels.SequenceEqual(originalPixels)).IsFalse();
            await Assert.That((await readPixels(originalUri)).SequenceEqual(originalPixels)).IsTrue();
            var rollback = revision(edited);
            rollback["checkpointId"] = checkpoint.GetProperty("checkpointId").GetString();
            JsonElement restored = await invoke("rollback_scene", rollback);
            await Assert.That(restored.GetProperty("generation").GetInt64())
                .IsEqualTo(edited.GetProperty("generation").GetInt64() + 1);
            (_, byte[] restoredPixels) = await capture(restored);
            await Assert.That(restoredPixels.SequenceEqual(originalPixels)).IsTrue();
            JsonElement incomplete = await invoke("finalize_scene", revision(restored));
            await Assert.That(incomplete.GetProperty("partial").GetBoolean()).IsTrue();
            string[] missing = incomplete.GetProperty("failures").EnumerateArray()
                .Select(static failure => failure.GetString()!).ToArray();
            await Assert.That(missing.Any(static failure =>
                failure.StartsWith("contact-sheet:", StringComparison.Ordinal)))
                .IsTrue();
            await Assert.That(missing.Any(static failure => failure.StartsWith("turntable:", StringComparison.Ordinal)))
                .IsTrue();
            _ = await capture(restored, "contact_sheet");
            _ = await capture(restored, "turntable");
            JsonElement final = await invoke("finalize_scene", revision(restored));
            await Assert.That(final.GetProperty("partial").GetBoolean()).IsFalse();
            await Assert.That(final.GetProperty("finalStageCreated").GetBoolean()).IsTrue();
            await Assert.That(final.GetProperty("failures").GetArrayLength()).IsEqualTo(0);
            await Assert.That(final.GetProperty("artifacts").GetArrayLength()).IsEqualTo(3);
            foreach (JsonElement report in final.GetProperty("artifacts").EnumerateArray())
            {
                ReadResourceResult resource = await client.ReadResourceAsync(report.GetProperty("uri").GetString()!,
                    cancellationToken: lifetime.Token);
                await Assert.That(resource.Contents).Count().IsEqualTo(1);
                await Assert.That(((TextResourceContents)resource.Contents[0]).Text.Length).IsGreaterThan(0);
            }
            _ = await invoke("close_scene", revision(restored));
            await Assert.That(gpuBudget.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(textureBudgets.CpuTextureBudget!.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(textureBudgets.GpuTextureBudget!.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(textureBudgets.GpuStagingBudget!.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That((await readPixels(originalUri)).SequenceEqual(originalPixels)).IsTrue();
            await Assert.That((await readPixels(editedUri)).SequenceEqual(editedPixels)).IsTrue();
            await Assert.That(await File.ReadAllTextAsync(files.SourcePath)).IsEqualTo(scene);

            async Task<JsonElement> invoke(string tool, Dictionary<string, object?> request)
            {
                CallToolResult result = await client.CallToolAsync(tool,
                    new Dictionary<string, object?> { ["request"] = request }, cancellationToken: lifetime.Token);
                await Assert.That(result.IsError).IsFalse();
                return result.StructuredContent ?? throw new InvalidDataException("Missing native protocol result.");
            }

            async Task<(string Uri, byte[] Pixels)> capture(JsonElement state, string kind = "still")
            {
                Dictionary<string, object?> request = previewRequest(state);
                request["kind"] = kind;
                JsonElement result = await invoke("render_preview", request);
                await Assert.That(result.GetProperty("stageRevision").GetUInt64())
                    .IsEqualTo(state.GetProperty("stageRevision").GetUInt64());
                await Assert.That(result.GetProperty("artifacts").GetArrayLength()).IsEqualTo(1);
                string uri = result.GetProperty("artifacts")[0].GetProperty("uri").GetString()!;
                return (uri, await readPixels(uri));
            }

            async Task<byte[]> readPixels(string uri)
            {
                ReadResourceResult result = await client.ReadResourceAsync(uri, cancellationToken: lifetime.Token);
                await Assert.That(result.Contents).Count().IsEqualTo(1);
                await Assert.That(result.Contents[0]).IsTypeOf<BlobResourceContents>();
                ImageRgba8 image = PngRgba8Decoder.Decode(((BlobResourceContents)result.Contents[0]).DecodedData.Span);
                await Assert.That(image.Width).IsEqualTo(64);
                await Assert.That(image.Height).IsEqualTo(64);
                return image.Pixels.ToArray();
            }
        }
        finally
        {
            await lifetime.CancelAsync();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(10));
        }

        static Dictionary<string, object?> revision(JsonElement state) => new()
        {
            ["sessionId"] = state.GetProperty("sessionId").GetString(),
            ["generation"] = state.GetProperty("generation").GetInt64(),
            ["stageRevision"] = state.GetProperty("stageRevision").GetUInt64()
        };

        static Dictionary<string, object?> previewRequest(JsonElement state)
        {
            Dictionary<string, object?> request = revision(state);
            request["kind"] = "still";
            request["cameraPath"] = "/Camera";
            request["width"] = 64;
            request["height"] = 64;
            request["views"] = new[] { new CaptureViewDto { Name = "native-protocol", TimeCode = 0 } };
            return request;
        }

        static async Task assertCenter(byte[] pixels, bool red)
        {
            int center = (32 * 64 + 32) * 4;
            await Assert.That((int)pixels[center + (red ? 0 : 1)])
                .IsGreaterThan(pixels[center + (red ? 1 : 0)] + 20);
            await Assert.That(pixels[center + (red ? 1 : 0)]).IsEqualTo((byte)0);
            await Assert.That(pixels[center + 2]).IsEqualTo((byte)0);
            await Assert.That(pixels[center + 3]).IsEqualTo(byte.MaxValue);
            int coverage = Enumerable.Range(0, pixels.Length / 4).Count(index =>
                pixels[index * 4 + (red ? 0 : 1)] > pixels[index * 4 + (red ? 1 : 0)] + 20 &&
                pixels[index * 4 + (red ? 0 : 1)] > pixels[index * 4 + 2] + 20 &&
                pixels[index * 4 + 3] >= 240);
            await Assert.That(coverage).IsGreaterThan(100);
        }
    }
}
