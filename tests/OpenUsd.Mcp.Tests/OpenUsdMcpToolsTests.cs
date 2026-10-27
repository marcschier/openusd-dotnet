// Copyright (c) marcschier. Licensed under the MIT License.

using System.IO.Pipelines;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenUsd.Rendering;

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
    [Arguments("success", false, "render_sequence")]
    [Arguments("success", true, "render_sequence")]
    [Arguments("refused", false, "render_sequence")]
    [Arguments("refused", true, "render_sequence")]
    [Arguments("cancelled", false, "render_sequence")]
    [Arguments("cancelled", true, "render_sequence")]
    [Arguments("pre-cancelled", false, "render_sequence")]
    [Arguments("success", true, "render_product")]
    [Arguments("refused", true, "render_product")]
    [Arguments("cancelled", true, "render_product")]
    [Arguments("success", true, "render_physics_sequence")]
    [Arguments("refused", true, "render_physics_sequence")]
    [Arguments("cancelled", true, "render_physics_sequence")]
    [Arguments("success", false, "render_preview")]
    [Arguments("success", true, "render_preview")]
    [Arguments("refused", true, "render_preview")]
    [Arguments("cancelled", true, "render_preview")]
    [Arguments("pre-cancelled", true, "render_preview")]
    public async Task RenderRequestProgressReportsReadinessOnlyAfterSuccessAndPreservesCancellationDrain(
        string outcome, bool encodedFrame, string toolName)
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelling = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = new RecordedProgress();
        var service = new FakeOpenUsdMcpService
        {
            RenderOperation = async token =>
            {
                try
                {
                    entered.TrySetResult();
                    if (outcome == "cancelled")
                    {
                        try
                        {
                            await Task.Delay(Timeout.Infinite, token);
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            cancelling.TrySetResult();
                            await release.Task;
                            throw;
                        }
                    }
                    await release.Task;
                    if (outcome == "refused")
                    {
                        throw new ArgumentException("The raster was refused.");
                    }
                    return new McpRenderSequenceResultDto(
                        "session-1", 2, 3, "job-1", "renders/job-1", "renders/job-1/manifest.json", 1, 100, []);
                }
                finally
                {
                    drained.TrySetResult();
                }
            }
        };
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOpenUsdMcpService>(service);
        services.AddSingleton(new OpenUsdMcpProtocolOptions());
        services.AddSingleton<IArtifactResourceStore>(new ArtifactResourceStore());
        services.AddMcpServer().WithStreamServerTransport(
            clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream()).WithOpenUsdTools();
        await using ServiceProvider provider = services.BuildServiceProvider();
        McpServer server = provider.GetRequiredService<McpServer>();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task serverTask = server.RunAsync(lifetime.Token);
        await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(
            clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()), cancellationToken: lifetime.Token);
        await using IAsyncDisposable progressRegistration = progress.Register(client, new ProgressToken("render"));
        if (outcome == "pre-cancelled")
        {
            cancellation.Cancel();
        }
        object request = toolName switch
        {
            "render_physics_sequence" => new
            {
                sessionId = "session-1",
                generation = 2,
                stageRevision = 3,
                scenarioIdentity = "progress-test",
                expectedMetersPerUnit = 1,
                expectedUpAxis = "Y"
            },
            "render_preview" => new
            {
                sessionId = "session-1",
                generation = 2,
                stageRevision = 3,
                kind = "turntable",
                views = new[] { new { name = "first" }, new { name = "second" } }
            },
            _ => new { sessionId = "session-1", generation = 2, stageRevision = 3 }
        };
        Task<CallToolResult> pending = client.SendRequestAsync<CallToolRequestParams, CallToolResult>(
            RequestMethods.ToolsCall, new CallToolRequestParams
            {
                Name = toolName,
                Meta = new System.Text.Json.Nodes.JsonObject { ["progressToken"] = "render" },
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["request"] = JsonSerializer.SerializeToElement(request)
                }
            }, requestId: new RequestId("render"),
            cancellationToken: outcome == "pre-cancelled" ? cancellation.Token : lifetime.Token).AsTask();
        try
        {
            if (outcome == "pre-cancelled")
            {
                await Assert.That(async () => await pending).Throws<OperationCanceledException>();
                await Assert.That(entered.Task.IsCompleted).IsFalse();
                await Assert.That(progress.Values).IsEmpty();
                return;
            }
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await progress.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(progress.Values.Length).IsEqualTo(1);
            await Assert.That(progress.Values[0].Progress).IsEqualTo(0f);
            await Assert.That(progress.Values[0].Message).Contains("checks pending");
            await Assert.That(service.LastCancellationToken.CanBeCanceled).IsTrue();
            if (encodedFrame)
            {
                Action<int, int> report = service.FrameEncoded ??
                    throw new InvalidOperationException("The frame progress observer was not supplied.");
                report(1, 2);
                await progress.FrameReported.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(progress.Values[1].Progress).IsEqualTo(1f / 3);
                await Assert.That(progress.Values[1].Total).IsEqualTo(1f);
                await Assert.That(progress.Values[1].Message)
                    .IsEqualTo(toolName == "render_preview"
                        ? "1/2 views captured; artifact registration not confirmed."
                        : "1/2 frames encoded in staging; publication not confirmed.");
                await Assert.That(pending.IsCompleted).IsFalse();
            }
            int priorReports = encodedFrame ? 2 : 1;
            if (outcome == "cancelled")
            {
                await client.SendMessageAsync(new JsonRpcNotification
                {
                    Method = NotificationMethods.CancelledNotification,
                    Params = JsonSerializer.SerializeToNode(
                        new CancelledNotificationParams { RequestId = new RequestId("render") })
                }, lifetime.Token);
                await cancelling.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(drained.Task.IsCompleted).IsFalse();
                await Assert.That(progress.Values.Length).IsEqualTo(priorReports);
                release.TrySetResult();
                await Assert.That(async () => await pending).Throws<OperationCanceledException>();
                await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(progress.Values.Length).IsEqualTo(priorReports);
                return;
            }
            release.TrySetResult();
            CallToolResult result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(result.IsError).IsEqualTo(outcome == "refused");
            if (outcome == "success")
            {
                await progress.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            await Assert.That(progress.Values.Length).IsEqualTo(priorReports + (outcome == "success" ? 1 : 0));
            if (outcome == "success")
            {
                await Assert.That(progress.Values[^1].Progress).IsEqualTo(1f);
                await Assert.That(progress.Values[^1].Total).IsEqualTo(1f);
                await Assert.That(progress.Values[^1].Message).IsEqualTo("Result ready.");
                await Assert.That(result.StructuredContent!.Value
                    .GetProperty(toolName == "render_preview" ? "requestId" : "jobId").GetString())
                    .IsEqualTo(toolName == "render_preview" ? "capture" : "job-1");
            }
            else
            {
                await Assert.That(result.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString())
                    .IsEqualTo("invalid_argument");
            }
        }
        finally
        {
            cancellation.Cancel();
            release.TrySetResult();
            lifetime.Cancel();
            try
            {
                await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
            }
        }
    }

    [Test]
    [Arguments("none")]
    [Arguments("defaults")]
    [Arguments("named")]
    public async Task RenderRequestDtoRetainsDisplayQualityAndUnresolvedOcioRequestsWithoutOpeningFiles(string mode)
    {
        using var files = new WorkspaceTestFiles();
        string config = Path.Combine(files.SourceRoot, "unopened.ocio");
        RenderDisplayTransform? transform = mode == "none" ? null : new RenderDisplayTransform(
            config, "scene-linear", mode == "named" ? "Display A" : null,
            mode == "named" ? "View B" : null, mode == "named" ? "Look C" : null,
            latticeSize: 8, shaperMinimumLog2: -12.5f, shaperMaximumLog2: 3.25f);
        StageRenderState state = StageRenderState.Create(new StageIdentity("original"))
            .WithViewport(new ViewportDimensions(257, 129))
            .WithDisplay(new SceneDisplayState(
                RenderPurpose.Proxy | RenderPurpose.Guide, RenderVisibility.IncludeInvisible,
                RenderDrawMode.HiddenSurfaceWireframe))
            .WithRenderSettings(new RenderSettings(7, false, false, new Vector4(0.125f, -0.25f, 2.5f, 0.75f),
                false, false, RenderComplexity.VeryHigh, RenderOutputTransform.Identity, 1.75f)
            {
                DisplayTransform = transform
            });
        McpRenderRequestDto dto = McpRenderRequestDto.From(state);
        state = state.WithDisplay(SceneDisplayState.Default).WithRenderSettings(RenderSettings.PresentationDefault);
        await Assert.That(state.RenderSettings.Exposure).IsEqualTo(-6);
        JsonElement json = JsonSerializer.SerializeToElement(dto, JsonSerializerOptions.Web);
        await Assert.That(json.GetProperty("width").GetInt32()).IsEqualTo(257);
        await Assert.That(json.GetProperty("height").GetInt32()).IsEqualTo(129);
        await Assert.That(json.GetProperty("purposes").GetString()).IsEqualTo("Proxy, Guide");
        await Assert.That(json.GetProperty("visibility").GetString()).IsEqualTo("IncludeInvisible");
        await Assert.That(json.GetProperty("drawMode").GetString()).IsEqualTo("HiddenSurfaceWireframe");
        await Assert.That(json.GetProperty("samplesPerPixel").GetInt32()).IsEqualTo(7);
        await Assert.That(json.GetProperty("enableLighting").GetBoolean()).IsFalse();
        await Assert.That(json.GetProperty("enableShadows").GetBoolean()).IsFalse();
        await Assert.That(json.GetProperty("backfaceCulling").GetBoolean()).IsFalse();
        await Assert.That(json.GetProperty("useSceneMaterials").GetBoolean()).IsFalse();
        await Assert.That(json.GetProperty("complexity").GetString()).IsEqualTo("VeryHigh");
        await Assert.That(json.GetProperty("outputTransform").GetString()).IsEqualTo("Identity");
        await Assert.That(json.GetProperty("exposure").GetSingle()).IsEqualTo(1.75f);
        await Assert.That(json.GetProperty("clearColor").EnumerateArray()
            .Select(static item => item.GetSingle()).SequenceEqual([0.125f, -0.25f, 2.5f, 0.75f])).IsTrue();
        JsonElement ocio = json.GetProperty("displayTransform");
        if (mode == "none")
        {
            await Assert.That(ocio.ValueKind).IsEqualTo(JsonValueKind.Null);
        }
        else
        {
            await Assert.That(ocio.GetProperty("configPath").GetString()).IsEqualTo(config);
            await Assert.That(ocio.GetProperty("sourceColorSpace").GetString()).IsEqualTo("scene-linear");
            await Assert.That(ocio.GetProperty("latticeSize").GetInt32()).IsEqualTo(8);
            await Assert.That(ocio.GetProperty("shaperMinimumLog2").GetSingle()).IsEqualTo(-12.5f);
            await Assert.That(ocio.GetProperty("shaperMaximumLog2").GetSingle()).IsEqualTo(3.25f);
            if (mode == "named")
            {
                await Assert.That(ocio.GetProperty("display").GetString()).IsEqualTo("Display A");
                await Assert.That(ocio.GetProperty("view").GetString()).IsEqualTo("View B");
                await Assert.That(ocio.GetProperty("look").GetString()).IsEqualTo("Look C");
            }
            else
            {
                await Assert.That(ocio.GetProperty("display").ValueKind).IsEqualTo(JsonValueKind.Null);
                await Assert.That(ocio.GetProperty("view").ValueKind).IsEqualTo(JsonValueKind.Null);
                await Assert.That(ocio.GetProperty("look").ValueKind).IsEqualTo(JsonValueKind.Null);
            }
        }
        await Assert.That(File.Exists(config)).IsFalse();
        await Assert.That(dto.ClearColor is IList<float>).IsFalse();
    }

    [Test]
    public async Task CameraRequestDtoRetainsEveryMatrixElementAndCopiesClipPlanes()
    {
        var view = new Matrix4x4(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16);
        var projection = new Matrix4x4(17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32);
        Vector4[] planes = [new(0.125f, -0.25f, 0.5f, 1.75f)];
        var camera = new CameraState(view, projection, planes);
        McpCameraRequestDto dto = McpCameraRequestDto.From(camera);
        planes[0] = new Vector4(999);
        await Assert.That(dto.View!.SequenceEqual(Enumerable.Range(1, 16).Select(static value => (float)value)))
            .IsTrue();
        await Assert.That(dto.Projection!.SequenceEqual(Enumerable.Range(17, 16).Select(static value => (float)value)))
            .IsTrue();
        await Assert.That(dto.ClipPlanes.Count).IsEqualTo(1);
        await Assert.That(dto.ClipPlanes[0].SequenceEqual([0.125f, -0.25f, 0.5f, 1.75f])).IsTrue();
        JsonElement json = JsonSerializer.SerializeToElement(dto, JsonSerializerOptions.Web);
        await Assert.That(json.GetProperty("mode").GetString()).IsEqualTo("matrices");
        await Assert.That(json.GetProperty("view").GetArrayLength()).IsEqualTo(16);
        await Assert.That(json.GetProperty("projection")[15].GetSingle()).IsEqualTo(32f);
        await Assert.That(json.GetProperty("clipPlanes")[0][1].GetSingle()).IsEqualTo(-0.25f);
    }

    [Test]
    [Arguments(null)]
    [Arguments("original-review-v3")]
    public async Task SimulationDtoRetainsTheOptionalOriginalHostReviewIdentity(string? reviewIdentity)
    {
        var identity = new RenderSimulationFrameIdentity(
            new Guid("969b9c7d-509e-44d3-8ee8-da48796c880c"), "recorded-scene",
            3, 17, 6, 0.25, 0, 1d / 24, 4, 2, 1, reviewIdentity);
        McpSimulationFrameDto dto = McpSimulationFrameDto.From(identity);
        await Assert.That(dto.ReviewIdentity).IsEqualTo(reviewIdentity);
        JsonElement json = JsonSerializer.SerializeToElement(dto, JsonSerializerOptions.Web);
        if (reviewIdentity is null)
        {
            await Assert.That(json.GetProperty("reviewIdentity").ValueKind).IsEqualTo(JsonValueKind.Null);
        }
        else
        {
            await Assert.That(json.GetProperty("reviewIdentity").GetString()).IsEqualTo(reviewIdentity);
        }
    }

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
    public async Task JobListingForwardsCancellationAndReturnsOnlyHistoricalStructuredMetadata()
    {
        var service = new FakeOpenUsdMcpService();
        var tools = new OpenUsdMcpTools(service, new OpenUsdMcpProtocolOptions());
        using var cancellation = new CancellationTokenSource();
        CallToolResult result = await tools.ListSequenceJobsAsync(cancellation.Token);
        await Assert.That(service.LastCancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(result.IsError).IsFalse();
        await Assert.That(result.StructuredContent!.Value.GetProperty("jobs").GetArrayLength()).IsEqualTo(1);
        await Assert.That(result.Content.Count).IsEqualTo(1);
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
            "list_sequence_jobs",
            "open_scene",
            "present_scene",
            "read_sequence_frame",
            "read_sequence_sheet",
            "render_physics_sequence",
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
        await Assert.That(tools.Single(static tool => tool.Name == "finalize_scene")
            .ProtocolTool.OutputSchema!.Value.GetProperty("properties").GetProperty("failures")
            .GetProperty("maxItems").GetInt32()).IsEqualTo(23);
        string editSchema = tools
            .Single(static tool => tool.Name == "apply_edits")
            .ProtocolTool
            .InputSchema
            .GetRawText();
        await Assert.That(renderSchema).Contains("\"cameraPath\"");
        string[] renderTools = ["render_preview", "render_sequence", "render_product", "render_physics_sequence"];
        foreach (string name in renderTools)
        {
            await Assert.That(tools.Single(tool => tool.Name == name).ProtocolTool.InputSchema
                .GetProperty("properties").EnumerateObject().Select(static property => property.Name))
                .IsEquivalentTo(["request"]);
        }
        await Assert.That(tools.Single(static tool => tool.Name == "render_sequence")
            .ProtocolTool.InputSchema.GetRawText()).Contains("\"hdrColorFormat\"");
        await Assert.That(tools.Single(static tool => tool.Name == "render_physics_sequence")
            .ProtocolTool.InputSchema.GetRawText()).Contains("\"expectedMetersPerUnit\"");
        await Assert.That(tools.Single(static tool => tool.Name == "open_scene")
            .ProtocolTool.InputSchema.GetRawText()).Contains("\"stageSettings\"");
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

        CallToolResult opened = await client.CallToolAsync("open_scene",
            new Dictionary<string, object?>
            {
                ["request"] = new Dictionary<string, object?>
                {
                    ["sourcePath"] = "scene.usda",
                    ["stageSettings"] = new Dictionary<string, object?>
                    {
                        ["metersPerUnit"] = 1,
                        ["kilogramsPerUnit"] = 2,
                        ["upAxis"] = "Z",
                        ["startTimeCode"] = 12,
                        ["endTimeCode"] = 252,
                        ["timeCodesPerSecond"] = 60,
                        ["framesPerSecond"] = 30
                    }
                }
            }, cancellationToken: cancellation.Token);
        await Assert.That(opened.IsError).IsFalse();
        await Assert.That(service.LastOpenRequest!.StageSettings!.ToSettings())
            .IsEqualTo(new WorkspaceStageSettings(1, 2, "Z", 12, 252, 60, 30));

        var previewProgress = new RecordedProgress();
        await using IAsyncDisposable previewProgressRegistration =
            previewProgress.Register(client, new ProgressToken("preview"));
        CallToolResult preview = await client.CallToolAsync("render_preview",
            new Dictionary<string, object?>
            {
                ["request"] = new
                {
                    sessionId = "session-1",
                    generation = 2,
                    stageRevision = 3,
                    kind = "still",
                    views = new[] { new { name = "main", timeCode = 0 } }
                }
            }, options: new RequestOptions { ProgressToken = new ProgressToken("preview") },
            cancellationToken: cancellation.Token);
        await Assert.That(preview.IsError).IsFalse();
        await previewProgress.AssertSuccessfulLifecycleAsync();

        var physicsProgress = new RecordedProgress();
        await using IAsyncDisposable physicsProgressRegistration =
            physicsProgress.Register(client, new ProgressToken("physics"));
        CallToolResult physics = await client.CallToolAsync("render_physics_sequence",
            new Dictionary<string, object?>
            {
                ["request"] = new Dictionary<string, object?>
                {
                    ["sessionId"] = "session-1",
                    ["generation"] = 2,
                    ["stageRevision"] = 3,
                    ["scenarioIdentity"] = "rigid-test",
                    ["expectedMetersPerUnit"] = 0.01,
                    ["expectedUpAxis"] = "Y",
                    ["stepsPerFrame"] = 4,
                    ["frameCount"] = 3
                }
            }, options: new RequestOptions { ProgressToken = new ProgressToken("physics") },
            cancellationToken: cancellation.Token);
        await physicsProgress.AssertSuccessfulLifecycleAsync();
        await Assert.That(physics.IsError).IsFalse();
        await Assert.That(physics.StructuredContent!.Value.GetProperty("jobId").GetString()).IsEqualTo("physics-1");
        await Assert.That(service.LastPhysicsRequest!.ScenarioIdentity).IsEqualTo("rigid-test");
        await Assert.That(service.LastPhysicsRequest.StepsPerFrame).IsEqualTo(4);
        await Assert.That(service.LastPhysicsRequest.ExpectedMetersPerUnit).IsEqualTo(0.01);

        var renderProgress = new RecordedProgress();
        await using IAsyncDisposable progressRegistration =
            renderProgress.Register(client, new ProgressToken("sequence"));
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
            }, options: new RequestOptions { ProgressToken = new ProgressToken("sequence") },
            cancellationToken: cancellation.Token);
        await Assert.That(sequence.IsError).IsFalse();
        await renderProgress.AssertSuccessfulLifecycleAsync();
        await Assert.That(sequence.StructuredContent!.Value.GetProperty("frameCount").GetInt32()).IsEqualTo(20);
        await Assert.That(sequence.StructuredContent!.Value.GetProperty("outputDirectory").GetString())
            .IsEqualTo("session-1/renders/job-1");
        await Assert.That(sequence.Content.Count).IsEqualTo(1);
        await Assert.That(service.LastSequenceRequest!.IncludeHdrColor).IsTrue();
        await Assert.That(service.LastSequenceRequest.HdrColorFormat).IsEqualTo("exr");

        var productProgress = new RecordedProgress();
        await using IAsyncDisposable productProgressRegistration =
            productProgress.Register(client, new ProgressToken("product"));
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
                    ["width"] = 7,
                    ["height"] = 5,
                    ["pixelAspectRatio"] = 1.5,
                    ["aspectRatioConformPolicy"] = "cropAperture",
                    ["dataWindowNdc"] = new float[] { -0.25f, 0, 1.25f, 1 },
                    ["frameCount"] = 3
                }
            }, options: new RequestOptions { ProgressToken = new ProgressToken("product") },
            cancellationToken: cancellation.Token);
        await productProgress.AssertSuccessfulLifecycleAsync();
        await Assert.That(product.IsError).IsFalse();
        await Assert.That(service.LastProductRequest!.Width).IsEqualTo(7);
        await Assert.That(service.LastProductRequest.Height).IsEqualTo(5);
        await Assert.That(service.LastProductRequest.PixelAspectRatio).IsEqualTo(1.5d);
        await Assert.That(service.LastProductRequest.AspectRatioConformPolicy).IsEqualTo("cropAperture");
        await Assert.That(service.LastProductRequest.DataWindowNdc!.SequenceEqual([-0.25f, 0, 1.25f, 1])).IsTrue();
        await Assert.That(product.StructuredContent!.Value.GetProperty("authoredProduct")
            .GetProperty("productPath").GetString()).IsEqualTo("/Render/Product");
        await Assert.That(product.StructuredContent.Value.GetProperty("authoredProduct")
            .GetProperty("outputs")[0].GetProperty("dataType").GetString()).IsEqualTo("half4");
        await Assert.That(product.StructuredContent.Value.GetProperty("frameCount").GetInt32()).IsEqualTo(3);
        await Assert.That(product.Content.Count).IsEqualTo(1);

        CallToolResult jobs = await client.CallToolAsync(
            "list_sequence_jobs", new Dictionary<string, object?>(), cancellationToken: cancellation.Token);
        await Assert.That(jobs.IsError).IsFalse();
        JsonElement listedJob = jobs.StructuredContent!.Value.GetProperty("jobs")[0];
        await Assert.That(listedJob.GetProperty("jobId").GetString()).IsEqualTo("historical-1");
        await Assert.That(listedJob.GetProperty("sessionId").GetString()).IsEqualTo("retired-session");
        await Assert.That(listedJob.GetProperty("generation").GetInt64()).IsEqualTo(7);
        await Assert.That(listedJob.GetProperty("stageRevision").GetUInt64()).IsEqualTo(11ul);
        await Assert.That(listedJob.GetProperty("frameCount").GetInt32()).IsEqualTo(3);
        await Assert.That(listedJob.GetProperty("outputDirectory").GetString())
            .IsEqualTo("retired-session/renders/historical-1");
        await Assert.That(listedJob.GetProperty("authoredProduct").GetProperty("productPath").GetString())
            .IsEqualTo("/Render/Historical");
        await Assert.That(listedJob.GetProperty("simulation").GetProperty("firstStep").GetUInt64()).IsEqualTo(6ul);
        await Assert.That(listedJob.GetProperty("simulation").GetProperty("lastStep").GetUInt64()).IsEqualTo(8ul);
        await Assert.That(listedJob.GetProperty("simulation").GetProperty("metersPerUnit").GetDouble())
            .IsEqualTo(0.01);
        await Assert.That(listedJob.GetProperty("simulation").GetProperty("upAxis").GetString()).IsEqualTo("Z");
        await Assert.That(listedJob.GetProperty("simulation").GetProperty("fullScenePhysics").GetBoolean()).IsTrue();
        await Assert.That(jobs.Content.Count).IsEqualTo(1);
        await Assert.That(jobs.Content[0]).IsTypeOf<TextContentBlock>();

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
        await Assert.That(sequenceFrame.StructuredContent.Value.GetProperty("simulation").ValueKind)
            .IsEqualTo(JsonValueKind.Null);
        JsonElement frameCamera = sequenceFrame.StructuredContent.Value.GetProperty("cameraRequest");
        await Assert.That(frameCamera.GetProperty("mode").GetString()).IsEqualTo("automatic");
        await Assert.That(frameCamera.GetProperty("view").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(frameCamera.GetProperty("projection").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(frameCamera.GetProperty("clipPlanes").GetArrayLength()).IsEqualTo(0);
        JsonElement frameRender = sequenceFrame.StructuredContent.Value.GetProperty("renderRequest");
        await Assert.That(frameRender.GetProperty("width").GetInt32()).IsEqualTo(1);
        await Assert.That(frameRender.GetProperty("outputTransform").GetString()).IsEqualTo("Reinhard");
        await Assert.That(frameRender.GetProperty("exposure").GetSingle()).IsEqualTo(-6);
        CallToolResult recordedFrame = await client.CallToolAsync("read_sequence_frame",
            new Dictionary<string, object?>
            {
                ["request"] = new Dictionary<string, object?>
                {
                    ["jobId"] = "recorded-job",
                    ["frameIndex"] = 0
                }
            }, cancellationToken: cancellation.Token);
        await Assert.That(recordedFrame.IsError).IsFalse();
        await Assert.That(recordedFrame.StructuredContent!.Value.GetProperty("simulation")
            .GetProperty("reviewIdentity").GetString()).IsEqualTo("recorded-review-v3");

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
        await Assert.That(sheet.StructuredContent.Value.GetProperty("authoredProduct")
            .GetProperty("productPath").GetString()).IsEqualTo("/Render/Product");
        JsonElement sheetSimulation = sheet.StructuredContent.Value.GetProperty("tiles")[0]
            .GetProperty("simulation");
        await Assert.That(sheetSimulation.GetProperty("epoch").GetGuid())
            .IsEqualTo(new Guid("969b9c7d-509e-44d3-8ee8-da48796c880c"));
        await Assert.That(sheetSimulation.GetProperty("scenarioIdentity").GetString()).IsEqualTo("recorded-scene");
        await Assert.That(sheetSimulation.GetProperty("sourceRevision").GetUInt64()).IsEqualTo(3ul);
        await Assert.That(sheetSimulation.GetProperty("publicationRevision").GetUInt64()).IsEqualTo(17ul);
        await Assert.That(sheetSimulation.GetProperty("stepIndex").GetUInt64()).IsEqualTo(6ul);
        await Assert.That(sheetSimulation.GetProperty("simulationSeconds").GetDouble()).IsEqualTo(0.25);
        await Assert.That(sheetSimulation.GetProperty("fixedStepSeconds").GetDouble()).IsEqualTo(1d / 24);
        await Assert.That(sheetSimulation.GetProperty("bindingRevision").GetUInt64()).IsEqualTo(4ul);
        await Assert.That(sheetSimulation.GetProperty("bodyCount").GetInt32()).IsEqualTo(2);
        await Assert.That(sheetSimulation.GetProperty("deformationCount").GetInt32()).IsEqualTo(1);
        await Assert.That(sheetSimulation.GetProperty("reviewIdentity").GetString()).IsEqualTo("recorded-review-v3");
        JsonElement sheetCamera = sheet.StructuredContent.Value.GetProperty("tiles")[0].GetProperty("cameraRequest");
        await Assert.That(sheetCamera.GetProperty("mode").GetString()).IsEqualTo("matrices");
        await Assert.That(sheetCamera.GetProperty("view").GetArrayLength()).IsEqualTo(16);
        await Assert.That(sheetCamera.GetProperty("view")[12].GetSingle()).IsEqualTo(1.25f);
        await Assert.That(sheetCamera.GetProperty("projection")[5].GetSingle()).IsEqualTo(2.5f);
        await Assert.That(sheetCamera.GetProperty("clipPlanes")[0][3].GetSingle()).IsEqualTo(-4f);
        JsonElement sheetRender = sheet.StructuredContent.Value.GetProperty("tiles")[0].GetProperty("renderRequest");
        await Assert.That(sheetRender.GetProperty("width").GetInt32()).IsEqualTo(257);
        await Assert.That(sheetRender.GetProperty("height").GetInt32()).IsEqualTo(129);
        await Assert.That(sheetRender.GetProperty("drawMode").GetString()).IsEqualTo("Wireframe");
        await Assert.That(sheetRender.GetProperty("outputTransform").GetString()).IsEqualTo("Identity");
        await Assert.That(sheetRender.GetProperty("exposure").GetSingle()).IsEqualTo(0);
        await Assert.That(sheet.Content.Count).IsEqualTo(2);
        await Assert.That(sheet.Content.OfType<ResourceLinkBlock>().Single().Uri)
            .IsEqualTo("openusd://artifact/sheet-0");

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
        await Assert.That(hdrFrame.StructuredContent.Value.GetProperty("renderRequest").ValueKind)
            .IsEqualTo(JsonValueKind.Null);

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

        foreach (string id in new[] { "request 1.txt", "capture-%2F-?.txt", "caf\u00e9.txt", new string('a', 1024) })
        {
            ArtifactResourceDescriptor descriptor = store.Add(id, "text/plain", Encoding.UTF8.GetBytes(id));
            ReadResourceResult escaped = await client.ReadResourceAsync(
                descriptor.ResourceUri.AbsoluteUri, cancellationToken: cancellation.Token);
            await Assert.That(escaped.Contents).Count().IsEqualTo(1);
            await Assert.That(escaped.Contents[0].Uri).IsEqualTo(descriptor.ResourceUri.AbsoluteUri);
            await Assert.That(((TextResourceContents)escaped.Contents[0]).Text).IsEqualTo(id);
        }

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

        RecordedProgress[] renderNotifications = [previewProgress, physicsProgress, renderProgress, productProgress];
        foreach (RecordedProgress recorded in renderNotifications)
        {
            await recorded.AssertSuccessfulLifecycleAsync();
        }
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

    internal sealed class RecordedProgress : IProgress<ProgressNotificationValue>
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<ProgressNotificationValue> _values = new();
        internal TaskCompletionSource Completed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource FrameReported { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ProgressNotificationValue[] Values => _values.ToArray();

        internal async Task AssertSuccessfulLifecycleAsync()
        {
            await Task.WhenAll(Started.Task, Completed.Task).WaitAsync(TimeSpan.FromSeconds(5));
            // The SDK dispatches notification callbacks concurrently, independently of wire order.
            ProgressNotificationValue[] values = Values.OrderBy(static value => value.Progress).ToArray();
            await Assert.That(values.Select(static value => value.Progress).SequenceEqual([0f, 1f])).IsTrue();
            await Assert.That(values.All(static value => value.Total == 1)).IsTrue();
            await Assert.That(values[0].Message).IsEqualTo("Request started; admission and revision checks pending.");
            await Assert.That(values[1].Message).IsEqualTo("Result ready.");
        }

        internal IAsyncDisposable Register(McpClient client, ProgressToken token) =>
            client.RegisterNotificationHandler(NotificationMethods.ProgressNotification, (notification, _) =>
            {
                if (notification.Params?.Deserialize<ProgressNotificationParams>() is { } value &&
                    value.ProgressToken == token)
                {
                    Report(value.Progress);
                }
                return ValueTask.CompletedTask;
            });

        public void Report(ProgressNotificationValue value)
        {
            _values.Enqueue(value);
            if (value.Progress == 0)
            {
                Started.TrySetResult();
            }
            if (value.Progress is > 0 and < 1)
            {
                FrameReported.TrySetResult();
            }
            if (value.Progress == 1)
            {
                Completed.TrySetResult();
            }
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
    internal Func<CancellationToken, ValueTask<McpRenderSequenceResultDto>>?
        RenderOperation
    { get; init; }
    internal McpCaptureResultDto? CaptureResult { get; init; }
    internal Action<int, int>? FrameEncoded { get; private set; }

    internal Exception? GetSceneException { get; init; }

    internal CancellationToken LastCancellationToken { get; private set; }
    internal RenderSequenceRequest? LastSequenceRequest { get; private set; }
    internal RenderProductCaptureRequest? LastProductRequest { get; private set; }
    internal RenderPhysicsSequenceRequest? LastPhysicsRequest { get; private set; }
    internal OpenSceneRequest? LastOpenRequest { get; private set; }

    public ValueTask<McpSessionDto> OpenSceneAsync(
        OpenSceneRequest request,
        CancellationToken cancellationToken)
    {
        LastOpenRequest = request;
        return ValueTask.FromResult(Session(cancellationToken));
    }

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

    public async ValueTask<McpCaptureResultDto> RenderPreviewAsync(
        RenderPreviewRequest request,
        CancellationToken cancellationToken, Action<int, int>? viewCaptured = null)
    {
        LastCancellationToken = cancellationToken;
        FrameEncoded = viewCaptured;
        if (RenderOperation is { } operation)
        {
            _ = await operation(cancellationToken);
        }
        return CaptureResult ??
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
                []);
    }

    public ValueTask<McpRenderSequenceResultDto> RenderSequenceAsync(
        RenderSequenceRequest request,
        CancellationToken cancellationToken, Action<int, int>? frameEncoded = null)
    {
        LastCancellationToken = cancellationToken;
        LastSequenceRequest = request;
        FrameEncoded = frameEncoded;
        if (RenderOperation is { } operation)
        {
            return operation(cancellationToken);
        }
        return ValueTask.FromResult(new McpRenderSequenceResultDto(
            request.SessionId, request.Generation, request.StageRevision, "job-1",
            "session-1/renders/job-1", "session-1/renders/job-1/manifest.json", request.FrameCount, 2048, []));
    }

    public ValueTask<McpRenderSequenceResultDto> RenderProductAsync(
        RenderProductCaptureRequest request, CancellationToken cancellationToken, Action<int, int>? frameEncoded = null)
    {
        LastProductRequest = request;
        LastCancellationToken = cancellationToken;
        FrameEncoded = frameEncoded;
        if (RenderOperation is { } operation)
        {
            return operation(cancellationToken);
        }
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

    public ValueTask<McpRenderSequenceResultDto> RenderPhysicsSequenceAsync(
        RenderPhysicsSequenceRequest request, CancellationToken cancellationToken,
        Action<int, int>? frameEncoded = null)
    {
        LastCancellationToken = cancellationToken;
        LastPhysicsRequest = request;
        FrameEncoded = frameEncoded;
        if (RenderOperation is { } operation)
        {
            return operation(cancellationToken);
        }
        return ValueTask.FromResult(new McpRenderSequenceResultDto(
            request.SessionId, request.Generation, request.StageRevision, "physics-1",
            "session-1/renders/physics-1", "session-1/renders/physics-1/manifest.json", request.FrameCount, 2048, []));
    }

    public ValueTask<McpSequenceJobsResultDto> ListSequenceJobsAsync(CancellationToken cancellationToken)
    {
        LastCancellationToken = cancellationToken;
        return ValueTask.FromResult(new McpSequenceJobsResultDto(
        [
            new McpRenderSequenceResultDto("retired-session", 7, 11, "historical-1",
                "retired-session/renders/historical-1", "retired-session/renders/historical-1/manifest.json",
                3, 2048, [])
            {
                AuthoredProduct = new McpAuthoredProductDto("/Render/Settings", "/Render/Historical", "/Camera",
                    "test-profile", "full", []),
                Simulation = new McpSimulationRecordingDto(
                    new Guid("969b9c7d-509e-44d3-8ee8-da48796c880c"), "historical-scenario", 6, 8, 1d / 24)
                {
                    MetersPerUnit = 0.01,
                    UpAxis = "Z",
                    FullScenePhysics = true
                }
            }
        ]));
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
            [
                new McpSequenceSheetTileDto(0, 0, 0, 0, request.Width, request.Height)
                {
                    Simulation = McpSimulationFrameDto.From(new RenderSimulationFrameIdentity(
                        new Guid("969b9c7d-509e-44d3-8ee8-da48796c880c"), "recorded-scene",
                        3, 17, 6, 0.25, 0, 1d / 24, 4, 2, 1, "recorded-review-v3")),
                    CameraRequest = McpCameraRequestDto.From(new CameraState(
                        Matrix4x4.CreateTranslation(1.25f, 2, 3), Matrix4x4.CreateScale(2.5f),
                        [new Vector4(0, 0, 1, -4)])),
                    RenderRequest = McpRenderRequestDto.From(StageRenderState.Default
                        .WithViewport(new ViewportDimensions(257, 129))
                        .WithDisplay(new SceneDisplayState(
                            RenderPurpose.Guide, RenderVisibility.IncludeInvisible, RenderDrawMode.Wireframe)))
                }
            ],
            [descriptor])
        {
            AuthoredProduct = new McpAuthoredProductDto("/Render/Settings", "/Render/Product", "/Camera",
                "test-profile", "full", [new McpProductVariableDto("/Render/Color", "color", "half4", "hdrColor")])
        });
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
            Array.AsReadOnly([descriptor]))
        {
            Simulation = request.JobId == "recorded-job"
                ? McpSimulationFrameDto.From(new RenderSimulationFrameIdentity(
                    new Guid("969b9c7d-509e-44d3-8ee8-da48796c880c"), "recorded-scene",
                    3, 17, 6, 0.25, 0, 1d / 24, 4, 2, 1, "recorded-review-v3"))
                : null,
            CameraRequest = McpCameraRequestDto.From(CameraState.Default),
            RenderRequest = McpRenderRequestDto.From(StageRenderState.Default
                .WithViewport(new ViewportDimensions(1, 1))
                .WithRenderSettings(RenderSettings.PresentationDefault))
        });
    }

    public ValueTask<McpAnalysisResultDto> AnalyzeSceneAsync(
        AnalyzeSceneRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new McpAnalysisResultDto("session-1", 2, 3, [], request.ProposalOffset, 0));

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
