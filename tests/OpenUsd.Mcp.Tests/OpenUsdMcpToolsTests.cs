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
            (new StageStatisticsQuotaExceededException(
                StageStatisticsLimitKind.PrimCount,
                1,
                2), "quota_exceeded"),
            (new ArtifactResourceStoreCapacityException("full"), "quota_exceeded"),
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
            "render_preview",
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
