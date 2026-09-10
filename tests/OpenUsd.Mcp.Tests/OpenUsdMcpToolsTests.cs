// Copyright (c) marcschier. Licensed under the MIT License.

using System.IO.Pipelines;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace OpenUsd.Mcp.Tests;

public sealed class OpenUsdMcpToolsTests
{
    private static readonly SceneRevisionRequest Revision = new()
    {
        SessionId = "session-1",
        Generation = 2,
        StageRevision = 3,
    };

    [Test]
    public async Task ForwardsCancellationAndReturnsStructuredContent()
    {
        var service = new FakeOpenUsdMcpService();
        var tools = new OpenUsdMcpTools(service, new OpenUsdMcpProtocolOptions());
        using var cancellation = new CancellationTokenSource();

        CallToolResult result = await tools.GetSceneAsync(Revision, cancellation.Token);

        await Assert.That(service.LastCancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(result.IsError).IsFalse();
        await Assert.That(result.StructuredContent).IsNotNull();
        await Assert.That(result.StructuredContent!.Value.GetProperty("sessionId").GetString())
            .IsEqualTo("session-1");
        await Assert.That(result.Content).Count().IsEqualTo(1);
        await Assert.That(result.Content[0]).IsTypeOf<TextContentBlock>();
    }

    [Test]
    public async Task ReturnsDeterministicStructuredErrors()
    {
        (Exception Exception, string Code)[] cases =
        [
            (new ArgumentException("bad input"), "invalid_argument"),
            (new OpenUsdMcpFailureException("path_denied", "denied"), "path_denied"),
            (new OpenUsdMcpFailureException("no_session", "none"), "no_session"),
            (new OpenUsdMcpFailureException("stale_session", "stale"), "stale_session"),
            (new OpenUsdMcpFailureException("stale_revision", "stale"), "stale_revision"),
            (new OpenUsdMcpFailureException("proposal_stale", "stale"), "proposal_stale"),
            (new WorkspacePathContainmentException("denied"), "path_denied"),
            (new WorkspaceQuotaExceededException("full"), "quota_exceeded"),
            (new OpenUsd.Rendering.RenderOutputQuotaExceededException("full"), "quota_exceeded"),
            (new StageStatisticsQuotaExceededException(
                StageStatisticsLimitKind.PrimCount,
                1,
                2), "quota_exceeded"),
            (new ArtifactResourceStoreCapacityException("full"), "quota_exceeded"),
            (new ArtifactResourceIntegrityException("changed"), "artifact_integrity_error"),
            (new DllNotFoundException("native"), "native_failure"),
            (new OpenUsdMcpFailureException("render_failure", "render"), "render_failure"),
            (new OpenUsdMcpFailureException("launch_failure", "launch"), "launch_failure"),
        ];

        foreach ((Exception exception, string expectedCode) in cases)
        {
            var service = new FakeOpenUsdMcpService
            {
                GetSceneException = exception,
            };
            var tools = new OpenUsdMcpTools(service, new OpenUsdMcpProtocolOptions());

            CallToolResult result = await tools.GetSceneAsync(Revision, default);

            await Assert.That(result.IsError).IsTrue();
            string? actualCode = result.StructuredContent!.Value
                .GetProperty("error")
                .GetProperty("code")
                .GetString();
            await Assert.That(actualCode).IsEqualTo(expectedCode);
        }
    }

    [Test]
    public async Task NativeFailuresProvideActionableSafeDiagnostics()
    {
        (Exception Exception, string Expected)[] cases =
        [
            (
                new DllNotFoundException("C:\\private\\usd_ms.dll"),
                "required native library could not be loaded"),
            (
                new BadImageFormatException("wrong architecture"),
                "wrong architecture or format"),
            (
                new EntryPointNotFoundException("missing export"),
                "native runtime ABI is incompatible"),
        ];

        foreach ((Exception exception, string expected) in cases)
        {
            var service = new FakeOpenUsdMcpService
            {
                GetSceneException = exception,
            };
            var tools = new OpenUsdMcpTools(service, new OpenUsdMcpProtocolOptions());

            CallToolResult result = await tools.GetSceneAsync(Revision, default);

            string message = result.StructuredContent!.Value
                .GetProperty("error")
                .GetProperty("message")
                .GetString()!;
            await Assert.That(message).Contains(expected);
            await Assert.That(message).DoesNotContain("C:\\private");
        }
    }

    [Test]
    public async Task UsesInlineImageOnlyWithinCapAndLinksLargerArtifacts()
    {
        byte[] small = [1, 2, 3];
        ArtifactResourceDescriptor inline = new(
            "small.png",
            ArtifactResourceUri.Create("small.png"),
            "image/png",
            small.Length,
            "small-hash",
            Convert.ToBase64String(small));
        ArtifactResourceDescriptor linked = new(
            "large.png",
            ArtifactResourceUri.Create("large.png"),
            "image/png",
            5,
            "large-hash",
            Convert.ToBase64String(new byte[5]));
        var service = new FakeOpenUsdMcpService
        {
            CaptureResult = new McpCaptureResultDto(
                "session-1",
                2,
                3,
                "capture",
                "still",
                1,
                1,
                Array.AsReadOnly(
                [
                    new McpArtifactDto(
                        inline.Id,
                        inline.ResourceUri.AbsoluteUri,
                        inline.MediaType,
                        inline.ByteLength,
                        inline.Sha256,
                        true),
                    new McpArtifactDto(
                        linked.Id,
                        linked.ResourceUri.AbsoluteUri,
                        linked.MediaType,
                        linked.ByteLength,
                        linked.Sha256,
                        true),
                ]),
                [],
                Array.AsReadOnly([inline, linked])),
        };
        var tools = new OpenUsdMcpTools(
            service,
            new OpenUsdMcpProtocolOptions(InlineImageMaximumBytes: 3));
        var request = new RenderPreviewRequest
        {
            SessionId = "session-1",
            Generation = 2,
            StageRevision = 3,
            Kind = "still",
            Width = 1,
            Height = 1,
            Views = [new CaptureViewDto { Name = "still" }],
        };

        CallToolResult result = await tools.RenderPreviewAsync(request, default);

        await Assert.That(result.Content).Count().IsEqualTo(3);
        await Assert.That(result.Content[1]).IsTypeOf<ImageContentBlock>();
        await Assert.That(result.Content[2]).IsTypeOf<ResourceLinkBlock>();
        var link = (ResourceLinkBlock)result.Content[2];
        await Assert.That(link.Uri).IsEqualTo("openusd://artifact/large.png");
    }

    [Test]
    public async Task ResourceProviderReturnsCorrectTextBlobAndNotFoundErrors()
    {
        var store = new ArtifactResourceStore();
        _ = store.Add(
            "report.json",
            "application/json",
            Encoding.UTF8.GetBytes("{\"ok\":true}"));
        _ = store.Add("preview.png", "image/png", new byte[] { 1, 2, 3 });
        _ = store.Add(
            "problem.json",
            "application/problem+json",
            Encoding.UTF8.GetBytes("{\"title\":\"problem\"}"));
        _ = store.Add("invalid.txt", "text/plain", new byte[] { 0xff });
        var resources = new OpenUsdMcpResources(store);

        ResourceContents text = await resources.ReadArtifactAsync("report.json", default);
        ResourceContents blob = await resources.ReadArtifactAsync("preview.png", default);
        ResourceContents problem = await resources.ReadArtifactAsync("problem.json", default);

        await Assert.That(text).IsTypeOf<TextResourceContents>();
        await Assert.That(((TextResourceContents)text).Text).IsEqualTo("{\"ok\":true}");
        await Assert.That(text.MimeType).IsEqualTo("application/json");
        await Assert.That(blob).IsTypeOf<BlobResourceContents>();
        await Assert.That(((BlobResourceContents)blob).DecodedData.ToArray())
            .IsEquivalentTo(new byte[] { 1, 2, 3 });
        await Assert.That(blob.MimeType).IsEqualTo("image/png");
        await Assert.That(problem).IsTypeOf<TextResourceContents>();
        await Assert.That(problem.MimeType).IsEqualTo("application/problem+json");
        await Assert.That(
                async () => await resources.ReadArtifactAsync("missing", default))
            .Throws<McpException>()
            .WithMessageContaining("artifact_not_found");
        await Assert.That(
                async () => await resources.ReadArtifactAsync("invalid.txt", default))
            .Throws<McpException>()
            .WithMessageContaining("artifact_invalid_text");
        await Assert.That(
                async () => await resources.ReadArtifactAsync("../report.json", default))
            .Throws<McpException>()
            .WithMessageContaining("invalid_argument");
    }

    [Test]
    public async Task ResourceProviderRejectsResponsesAboveConfiguredReadLimit()
    {
        var store = new ArtifactResourceStore(
            new ArtifactResourceStoreOptions(
                MaximumReadResponseBytes: 3));
        _ = store.Add(
            "too-large.bin",
            "application/octet-stream",
            new byte[] { 1, 2, 3, 4 });
        var resources = new OpenUsdMcpResources(store);

        await Assert.That(
                async () => await resources.ReadArtifactAsync(
                    "too-large.bin",
                    default))
            .Throws<McpException>()
            .WithMessageContaining("artifact_too_large");
    }

    [Test]
    public async Task OfficialProtocolDiscoversSchemasInvokesAndReadsResources()
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var service = new FakeOpenUsdMcpService();
        var store = new ArtifactResourceStore();
        _ = store.Add("protocol.txt", "text/plain", Encoding.UTF8.GetBytes("protocol artifact"));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOpenUsdMcpService>(service);
        services.AddSingleton(new OpenUsdMcpProtocolOptions());
        services.AddSingleton<IArtifactResourceStore>(store);
        services.AddMcpServer()
            .WithStreamServerTransport(
                clientToServer.Reader.AsStream(),
                serverToClient.Writer.AsStream())
            .WithOpenUsdTools();
        await using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
        McpServer server = provider.GetRequiredService<McpServer>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task serverTask = server.RunAsync(cancellation.Token);
        await using McpClient client = await McpClient.CreateAsync(
            new StreamClientTransport(
                clientToServer.Writer.AsStream(),
                serverToClient.Reader.AsStream()),
            cancellationToken: cancellation.Token);

        IList<McpClientTool> tools = await client.ListToolsAsync(
            cancellationToken: cancellation.Token);
        string[] expectedNames =
        [
            "analyze_scene",
            "apply_edits",
            "apply_proposals",
            "checkpoint_scene",
            "close_scene",
            "finalize_scene",
            "get_scene",
            "inspect_scene",
            "open_scene",
            "present_scene",
            "read_sequence_frame",
            "read_sequence_sheet",
            "render_preview",
            "render_product",
            "render_sequence",
            "rollback_scene",
        ];

        await Assert.That(tools.Select(static tool => tool.Name).Order(StringComparer.Ordinal))
            .IsEquivalentTo(expectedNames);
        foreach (McpClientTool tool in tools)
        {
            await Assert.That(tool.ProtocolTool.Description).IsNotNull();
            await Assert.That(tool.ProtocolTool.Description!).Contains("Preconditions:");
            await Assert.That(tool.ProtocolTool.Description!).Contains("Result bounds:");
            await Assert.That(tool.ProtocolTool.Description!).Contains("Errors:");
            await Assert.That(tool.ProtocolTool.Description!).Contains("Example arguments:");
            await Assert.That(tool.ProtocolTool.InputSchema.ValueKind)
                .IsEqualTo(System.Text.Json.JsonValueKind.Object);
            await Assert.That(tool.ProtocolTool.OutputSchema).IsNotNull();
            await AssertPropertyDescriptionsAsync(tool.ProtocolTool.InputSchema);
            await AssertPropertyDescriptionsAsync(tool.ProtocolTool.OutputSchema!.Value);
        }

        string renderSchema = tools
            .Single(static tool => tool.Name == "render_preview")
            .ProtocolTool
            .InputSchema
            .GetRawText();
        string editSchema = tools
            .Single(static tool => tool.Name == "apply_edits")
            .ProtocolTool
            .InputSchema
            .GetRawText();
        await Assert.That(renderSchema).Contains("\"cameraPath\"");
        await Assert.That(tools.Single(static tool => tool.Name == "render_sequence")
            .ProtocolTool.InputSchema.GetRawText()).Contains("\"hdrColorFormat\"");
        foreach (string propertyName in new[]
                 {
                     "boolValue",
                     "int64Value",
                     "stringValue",
                     "vectorValue",
                 })
        {
            await Assert.That(editSchema).Contains($"\"{propertyName}\"");
        }

        CallToolResult call = await client.CallToolAsync(
            "get_scene",
            new Dictionary<string, object?>
            {
                ["request"] = new Dictionary<string, object?>
                {
                    ["sessionId"] = "session-1",
                    ["generation"] = 2,
                    ["stageRevision"] = 3,
                },
            },
            cancellationToken: cancellation.Token);
        await Assert.That(call.IsError).IsFalse();
        await Assert.That(call.StructuredContent!.Value.GetProperty("sessionId").GetString())
            .IsEqualTo("session-1");

        CallToolResult sequence = await client.CallToolAsync("render_sequence",
            new Dictionary<string, object?>
            {
                ["request"] = new Dictionary<string, object?>
                {
                    ["sessionId"] = "session-1",
                    ["generation"] = 2,
                    ["stageRevision"] = 3,
                    ["frameCount"] = 20,
                    ["width"] = 2,
                    ["height"] = 1,
                    ["startTimeCode"] = 2,
                    ["timeStep"] = 0.5,
                    ["includeHdrColor"] = true,
                    ["hdrColorFormat"] = "exr"
                }
            }, cancellationToken: cancellation.Token);
        await Assert.That(sequence.IsError).IsFalse();
        await Assert.That(sequence.StructuredContent!.Value.GetProperty("frameCount").GetInt32()).IsEqualTo(20);
        await Assert.That(sequence.StructuredContent!.Value.GetProperty("outputDirectory").GetString())
            .IsEqualTo("session-1/renders/job-1");
        await Assert.That(sequence.Content.Count).IsEqualTo(1);
        await Assert.That(service.LastSequenceRequest!.IncludeHdrColor).IsTrue();
        await Assert.That(service.LastSequenceRequest.HdrColorFormat).IsEqualTo("exr");

        CallToolResult product = await client.CallToolAsync("render_product",
            new Dictionary<string, object?>
            {
                ["request"] = new Dictionary<string, object?>
                {
                    ["sessionId"] = "session-1",
                    ["generation"] = 2,
                    ["stageRevision"] = 3,
                    ["settingsPath"] = "/Render/Settings",
                    ["productPath"] = "/Render/Product",
                    ["frameCount"] = 3
                }
            }, cancellationToken: cancellation.Token);
        await Assert.That(product.IsError).IsFalse();
        await Assert.That(product.StructuredContent!.Value.GetProperty("authoredProduct")
            .GetProperty("productPath").GetString()).IsEqualTo("/Render/Product");
        await Assert.That(product.StructuredContent.Value.GetProperty("authoredProduct")
            .GetProperty("outputs")[0].GetProperty("dataType").GetString()).IsEqualTo("half4");
        await Assert.That(product.StructuredContent.Value.GetProperty("frameCount").GetInt32()).IsEqualTo(3);
        await Assert.That(product.Content.Count).IsEqualTo(1);

        CallToolResult sequenceFrame = await client.CallToolAsync("read_sequence_frame",
            new Dictionary<string, object?>
            {
                ["request"] = new Dictionary<string, object?>
                {
                    ["jobId"] = "job-1",
                    ["frameIndex"] = 0
                }
            }, cancellationToken: cancellation.Token);
        await Assert.That(sequenceFrame.IsError).IsFalse();
        await Assert.That(sequenceFrame.StructuredContent!.Value.GetProperty("frameIndex").GetInt32()).IsEqualTo(0);
        await Assert.That(sequenceFrame.Content.Count).IsEqualTo(2);
        await Assert.That(sequenceFrame.Content[1]).IsTypeOf<ResourceLinkBlock>();

        int[] sheetIndices = [0];
        CallToolResult sheet = await client.CallToolAsync("read_sequence_sheet",
            new Dictionary<string, object?>
            {
                ["request"] = new Dictionary<string, object?>
                {
                    ["jobId"] = "job-1",
                    ["frameIndices"] = sheetIndices,
                    ["width"] = 32,
                    ["height"] = 16
                }
            }, cancellationToken: cancellation.Token);
        await Assert.That(sheet.IsError).IsFalse();
        await Assert.That(sheet.StructuredContent!.Value.GetProperty("width").GetInt32()).IsEqualTo(32);
        await Assert.That(sheet.StructuredContent.Value.GetProperty("tiles")[0]
            .GetProperty("frameIndex").GetInt32()).IsEqualTo(0);
        await Assert.That(sheet.Content.Count).IsEqualTo(2);
        await Assert.That(sheet.Content.OfType<ResourceLinkBlock>().Single().Uri).IsEqualTo("openusd://artifact/sheet-0");

        CallToolResult hdrFrame = await client.CallToolAsync("read_sequence_frame",
            new Dictionary<string, object?>
            {
                ["request"] = new Dictionary<string, object?>
                {
                    ["jobId"] = "hdr-job",
                    ["frameIndex"] = 0
                }
            }, cancellationToken: cancellation.Token);
        await Assert.That(hdrFrame.IsError).IsFalse();
        await Assert.That(hdrFrame.Content.Count).IsEqualTo(4);
        await Assert.That(hdrFrame.Content.OfType<ResourceLinkBlock>().Select(static link => link.Uri))
            .IsEquivalentTo(["openusd://artifact/frame-0", "openusd://artifact/depth-0", "openusd://artifact/hdr-0"]);
        await Assert.That(hdrFrame.StructuredContent!.Value.GetProperty("hdrColor").GetProperty("uri").GetString())
            .IsEqualTo("openusd://artifact/hdr-0");
        await Assert.That(hdrFrame.StructuredContent.Value.GetProperty("hdrColorFormat").GetString()).IsEqualTo("raw");

        CallToolResult exrFrame = await client.CallToolAsync("read_sequence_frame",
            new Dictionary<string, object?>
            {
                ["request"] = new Dictionary<string, object?> { ["jobId"] = "exr-job", ["frameIndex"] = 0 }
            }, cancellationToken: cancellation.Token);
        await Assert.That(exrFrame.IsError).IsFalse();
        await Assert.That(exrFrame.Content.Count).IsEqualTo(4);
        await Assert.That(exrFrame.Content.OfType<ResourceLinkBlock>()
            .Single(static link => link.MimeType == "image/x-exr").Uri).IsEqualTo("openusd://artifact/hdr-0.exr");
        await Assert.That(exrFrame.StructuredContent!.Value.GetProperty("hdrColorFormat").GetString()).IsEqualTo("exr");
        await Assert.That(exrFrame.StructuredContent.Value.GetProperty("hdrColor").GetProperty("mimeType").GetString())
            .IsEqualTo("image/x-exr");

        ReadResourceResult resource = await client.ReadResourceAsync(
            "openusd://artifact/protocol.txt",
            cancellationToken: cancellation.Token);
        await Assert.That(resource.Contents).Count().IsEqualTo(1);
        await Assert.That(resource.Contents[0]).IsTypeOf<TextResourceContents>();
        await Assert.That(((TextResourceContents)resource.Contents[0]).Text)
            .IsEqualTo("protocol artifact");

        IList<McpClientResourceTemplate> templates =
            await client.ListResourceTemplatesAsync(cancellationToken: cancellation.Token);
        McpClientResourceTemplate artifactTemplate = templates.Single();
        await Assert.That(artifactTemplate.ProtocolResourceTemplate.UriTemplate)
            .IsEqualTo("openusd://artifact/{id}");
        await Assert.That(artifactTemplate.ProtocolResourceTemplate.Description)
            .Contains("Preconditions:");
        await Assert.That(artifactTemplate.ProtocolResourceTemplate.Description)
            .Contains("Errors:");
        await Assert.That(artifactTemplate.ProtocolResourceTemplate.Description)
            .Contains("Example URI:");

        await cancellation.CancelAsync();
        await Assert.That(async () => await serverTask).ThrowsNothing();
    }

    [Test]
    public async Task McpSourcesDoNotWriteDirectlyToConsole()
    {
        string root = FindRepositoryRoot();
        string[] sources = Directory.GetFiles(
            Path.Combine(root, "src", "OpenUsd.Mcp"),
            "*.cs",
            SearchOption.TopDirectoryOnly);

        foreach (string source in sources)
        {
            string content = await File.ReadAllTextAsync(source);
            await Assert.That(content).DoesNotContain("Console.Write");
        }
    }

    private static string FindRepositoryRoot()
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

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }

    private static async ValueTask AssertPropertyDescriptionsAsync(
        System.Text.Json.JsonElement schema)
    {
        if (schema.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (schema.TryGetProperty("properties", out System.Text.Json.JsonElement properties))
            {
                foreach (System.Text.Json.JsonProperty property in properties.EnumerateObject())
                {
                    await Assert.That(property.Value.TryGetProperty("description", out _))
                        .IsTrue()
                        .Because($"Schema property '{property.Name}' must be self-documenting.");
                }
            }

            foreach (System.Text.Json.JsonProperty property in schema.EnumerateObject())
            {
                await AssertPropertyDescriptionsAsync(property.Value);
            }
        }
        else if (schema.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (System.Text.Json.JsonElement item in schema.EnumerateArray())
            {
                await AssertPropertyDescriptionsAsync(item);
            }
        }
    }
}

internal sealed class FakeOpenUsdMcpService : IOpenUsdMcpService
{
    internal McpCaptureResultDto? CaptureResult { get; init; }

    internal Exception? GetSceneException { get; init; }

    internal CancellationToken LastCancellationToken { get; private set; }
    internal RenderSequenceRequest? LastSequenceRequest { get; private set; }

    public ValueTask<McpSessionDto> OpenSceneAsync(
        OpenSceneRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(Session(cancellationToken));

    public ValueTask<McpClosedSceneDto> CloseSceneAsync(
        SceneRevisionRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new McpClosedSceneDto(request.SessionId, true));

    public ValueTask<McpSessionDto> GetSceneAsync(
        SceneRevisionRequest request,
        CancellationToken cancellationToken)
    {
        LastCancellationToken = cancellationToken;
        return GetSceneException is null
            ? ValueTask.FromResult(Session(cancellationToken))
            : ValueTask.FromException<McpSessionDto>(GetSceneException);
    }

    public ValueTask<McpSceneInspectionDto> InspectSceneAsync(
        SceneRevisionRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(
            new McpSceneInspectionDto(
                Session(cancellationToken),
                0,
                1,
                "SessionCreated",
                "/World",
                1,
                0,
                0,
                0,
                0,
                1,
                1,
                0));

    public ValueTask<McpEditResultDto> ApplyEditsAsync(
        ApplyEditsRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new McpEditResultDto("session-1", 3, 4, "checkpoint", 1));

    public ValueTask<McpCheckpointResultDto> CheckpointSceneAsync(
        SceneRevisionRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new McpCheckpointResultDto("session-1", 2, 3, "checkpoint"));

    public ValueTask<McpRollbackResultDto> RollbackSceneAsync(
        RollbackSceneRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new McpRollbackResultDto("session-1", 3, 4, request.CheckpointId));

    public ValueTask<McpCaptureResultDto> RenderPreviewAsync(
        RenderPreviewRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(
            CaptureResult ??
            new McpCaptureResultDto(
                "session-1",
                2,
                3,
                "capture",
                "still",
                1,
                1,
                [],
                [],
                []));

    public ValueTask<McpRenderSequenceResultDto> RenderSequenceAsync(
        RenderSequenceRequest request,
        CancellationToken cancellationToken)
    {
        LastCancellationToken = cancellationToken;
        LastSequenceRequest = request;
        return ValueTask.FromResult(new McpRenderSequenceResultDto(
            request.SessionId, request.Generation, request.StageRevision, "job-1",
            "session-1/renders/job-1", "session-1/renders/job-1/manifest.json", request.FrameCount, 2048, []));
    }

    public ValueTask<McpRenderSequenceResultDto> RenderProductAsync(
        RenderProductCaptureRequest request, CancellationToken cancellationToken)
    {
        LastCancellationToken = cancellationToken;
        return ValueTask.FromResult(new McpRenderSequenceResultDto(
            request.SessionId, request.Generation, request.StageRevision, "product-1",
            "session-1/renders/product-1", "session-1/renders/product-1/manifest.json", request.FrameCount, 2048, [])
        {
            AuthoredProduct = new McpAuthoredProductDto(request.SettingsPath ?? "/Render/Settings",
                request.ProductPath ?? "/Render/Product", request.CameraPath ?? "/Camera",
                "raw-raster-split-planes-v1", "full",
                [new McpProductVariableDto("/Render/Color", "color", "half4", "hdrColor")])
        });
    }

    public ValueTask<McpSequenceSheetResultDto> ReadSequenceSheetAsync(
        ReadSequenceSheetRequest request, CancellationToken cancellationToken)
    {
        LastCancellationToken = cancellationToken;
        var descriptor = new ArtifactResourceDescriptor(
            "sheet-0", ArtifactResourceUri.Create("sheet-0"), "image/png", 128, new string('a', 64));
        return ValueTask.FromResult(new McpSequenceSheetResultDto(
            "session-1", 2, 3, request.JobId, request.Width, request.Height,
            new McpArtifactDto(descriptor.Id, descriptor.ResourceUri.AbsoluteUri,
                descriptor.MediaType, descriptor.ByteLength, descriptor.Sha256, false),
            [new McpSequenceSheetTileDto(0, 0, 0, 0, request.Width, request.Height)],
            [descriptor]));
    }

    public ValueTask<McpSequenceFrameResultDto> ReadSequenceFrameAsync(
        ReadSequenceFrameRequest request,
        CancellationToken cancellationToken)
    {
        LastCancellationToken = cancellationToken;
        var descriptor = new ArtifactResourceDescriptor(
            "frame-0", ArtifactResourceUri.Create("frame-0"), "image/png", 128, new string('a', 64));
        if (request.JobId is "hdr-job" or "exr-job")
        {
            bool exr = request.JobId == "exr-job";
            var depth = new ArtifactResourceDescriptor("depth-0", ArtifactResourceUri.Create("depth-0"),
                "application/octet-stream", 4, new string('b', 64));
            string hdrId = exr ? "hdr-0.exr" : "hdr-0";
            var hdr = new ArtifactResourceDescriptor(hdrId, ArtifactResourceUri.Create(hdrId),
                exr ? "image/x-exr" : "application/octet-stream", exr ? 512 : 8, new string('c', 64));
            return ValueTask.FromResult(new McpSequenceFrameResultDto("session-1", 2, 3, request.JobId,
                request.FrameIndex, 0, 1, 1,
                new McpArtifactDto(descriptor.Id, descriptor.ResourceUri.AbsoluteUri,
                    descriptor.MediaType, descriptor.ByteLength, descriptor.Sha256, false),
                Array.AsReadOnly([descriptor, depth, hdr]))
            {
                DeviceDepth = new McpArtifactDto(depth.Id, depth.ResourceUri.AbsoluteUri,
                    depth.MediaType, depth.ByteLength, depth.Sha256, false),
                HdrColor = new McpArtifactDto(hdr.Id, hdr.ResourceUri.AbsoluteUri,
                    hdr.MediaType, hdr.ByteLength, hdr.Sha256, false),
                HdrColorFormat = exr ? "exr" : "raw"
            });
        }
        return ValueTask.FromResult(new McpSequenceFrameResultDto("session-1", 2, 3, request.JobId,
            request.FrameIndex, 0, 1, 1,
            new McpArtifactDto(descriptor.Id, descriptor.ResourceUri.AbsoluteUri,
                descriptor.MediaType, descriptor.ByteLength, descriptor.Sha256, false),
            Array.AsReadOnly([descriptor])));
    }

    public ValueTask<McpAnalysisResultDto> AnalyzeSceneAsync(
        AnalyzeSceneRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new McpAnalysisResultDto("session-1", 2, 3, []));

    public ValueTask<McpApplyProposalsResultDto> ApplyProposalsAsync(
        ApplyProposalsRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(
            new McpApplyProposalsResultDto(
                "session-1",
                3,
                4,
                request.ProposalIds,
                "checkpoint"));

    public ValueTask<McpFinalizationResultDto> FinalizeSceneAsync(
        SceneRevisionRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(
            new McpFinalizationResultDto("session-1", 2, 3, false, true, [], [], []));

    public ValueTask<McpPresentationResultDto> PresentSceneAsync(
        PresentSceneRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(
            new McpPresentationResultDto("session-1", 42, DateTimeOffset.UnixEpoch, request.Renderer));

    private McpSessionDto Session(CancellationToken cancellationToken)
    {
        LastCancellationToken = cancellationToken;
        return new McpSessionDto(
            "session-1",
            2,
            3,
            "scene.usda",
            DateTimeOffset.UnixEpoch);
    }
}
