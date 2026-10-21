// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace OpenUsd.Mcp.Tests;

public sealed class WorkspaceStageSettingsTests
{
    private const string RequestJson = """
        {
            "sourcePath": "scene.usda",
            "stageSettings": {
                "metersPerUnit": 0.125,
                "kilogramsPerUnit": 2.5,
                "upAxis": "Z",
                "startTimeCode": -12.5,
                "endTimeCode": 252.25,
                "timeCodesPerSecond": 60,
                "framesPerSecond": 30
            }
        }
        """;

    [Test]
    public async Task StageSettingsWriteExactInvariantRootFieldsWithoutOverwritingFiles()
    {
        using var files = new WorkspaceTestFiles();
        string path = Path.Combine(files.OutputRoot, "overlay.usda");
        WorkspaceStageSettings settings = JsonSerializer.Deserialize<OpenSceneRequest>(RequestJson)!
            .StageSettings!.ToSettings();
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            await OpenUsdWorkspaceSessionBackendFactory.WriteStageSettingsAsync(path, settings, default);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
        const string expected = """
            #usda 1.0
            (
                metersPerUnit = 0.125
                kilogramsPerUnit = 2.5
                upAxis = "Z"
                startTimeCode = -12.5
                endTimeCode = 252.25
                timeCodesPerSecond = 60
                framesPerSecond = 30
            )
            """ + "\n";
        byte[] bytes = await File.ReadAllBytesAsync(path);
        await Assert.That(bytes.SequenceEqual(Encoding.UTF8.GetBytes(expected))).IsTrue();
        await Assert.That(async () => await OpenUsdWorkspaceSessionBackendFactory.WriteStageSettingsAsync(
            path, new WorkspaceStageSettings(1, 1, "Y", 0, 0, 24, 24), default)).Throws<IOException>();
        await Assert.That((await File.ReadAllBytesAsync(path)).SequenceEqual(bytes)).IsTrue();
    }

    [Test]
    public async Task CancelledStageSettingsWriteCreatesNoFile()
    {
        using var files = new WorkspaceTestFiles();
        string path = Path.Combine(files.OutputRoot, "overlay.usda");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.That(async () => await OpenUsdWorkspaceSessionBackendFactory.WriteStageSettingsAsync(
            path, new WorkspaceStageSettings(1, 1, "Z", 0, 240, 60, 60), cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(File.Exists(path)).IsFalse();
    }

    [Test]
    [Arguments("metersPerUnit")]
    [Arguments("kilogramsPerUnit")]
    [Arguments("timeCodesPerSecond")]
    [Arguments("framesPerSecond")]
    public async Task StageSettingsRefuseNonpositiveAndNonfiniteMetrics(string field)
    {
        foreach (double value in new[] { 0d, -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            ArgumentOutOfRangeException? error = await Assert.That(() => new WorkspaceStageSettings(
                field == "metersPerUnit" ? value : 1,
                field == "kilogramsPerUnit" ? value : 1,
                "Z", 0, 240,
                field == "timeCodesPerSecond" ? value : 60,
                field == "framesPerSecond" ? value : 60)).Throws<ArgumentOutOfRangeException>();
            await Assert.That(error!.ParamName).IsEqualTo(field);
        }
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("X")]
    [Arguments("z")]
    [Arguments("Z\"\n)")]
    public async Task StageSettingsRefuseInvalidAxes(string? axis) =>
        await Assert.That(() => new WorkspaceStageSettings(1, 1, axis!, 0, 240, 60, 60))
            .Throws<ArgumentException>();

    [Test]
    [Arguments(double.NaN, 1)]
    [Arguments(double.PositiveInfinity, double.PositiveInfinity)]
    [Arguments(double.NegativeInfinity, 1)]
    [Arguments(0, double.NaN)]
    [Arguments(0, double.PositiveInfinity)]
    [Arguments(0, double.NegativeInfinity)]
    [Arguments(1, 0)]
    [Arguments(-double.MaxValue, double.MaxValue)]
    public async Task StageSettingsRefuseInvalidTimeline(double start, double end) =>
        await Assert.That(() => new WorkspaceStageSettings(1, 1, "Y", start, end, 24, 24))
            .Throws<ArgumentOutOfRangeException>();

    [Test]
    public async Task StageSettingsAllowNegativeAndSingleTimeCodeRanges()
    {
        var settings = new WorkspaceStageSettings(1, 1, "Y", -12.5, -12.5, 24, 30);
        await Assert.That(settings.StartTimeCode).IsEqualTo(-12.5);
        await Assert.That(settings.EndTimeCode).IsEqualTo(-12.5);
        await Assert.That(settings.TimeCodesPerSecond).IsEqualTo(24d);
        await Assert.That(settings.FramesPerSecond).IsEqualTo(30d);
    }

    [Test]
    [Arguments("metersPerUnit")]
    [Arguments("kilogramsPerUnit")]
    [Arguments("upAxis")]
    [Arguments("startTimeCode")]
    [Arguments("endTimeCode")]
    [Arguments("timeCodesPerSecond")]
    [Arguments("framesPerSecond")]
    public async Task StageSettingsJsonRequiresEveryExplicitField(string field)
    {
        JsonNode request = JsonNode.Parse(RequestJson)!;
        request["stageSettings"]!.AsObject().Remove(field);
        await Assert.That(() => JsonSerializer.Deserialize<OpenSceneRequest>(request.ToJsonString()))
            .Throws<JsonException>();
    }

    [Test]
    public async Task StageSettingsJsonRefusesArbitraryMetadataKeys()
    {
        JsonNode request = JsonNode.Parse(RequestJson)!;
        request["stageSettings"]!["customLayerData"] = "not admitted";
        await Assert.That(() => JsonSerializer.Deserialize<OpenSceneRequest>(request.ToJsonString()))
            .Throws<JsonException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OpenSceneForwardsOnlyExplicitStageSettings(bool configure)
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using McpSessionWorkspace workspace = files.CreateWorkspace(backend);
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, new OpenUsdMcpApplicationOptions(
            files.SourceRoot, files.OutputRoot, "unused", files.OutputRoot, "unused"));
        OpenSceneRequest request = JsonSerializer.Deserialize<OpenSceneRequest>(
            configure ? RequestJson : """{"sourcePath":"scene.usda"}""")!;
        McpSessionDto session = await service.OpenSceneAsync(request, default);
        await Assert.That(backend.Context!.StageSettings).IsEqualTo(configure
            ? new WorkspaceStageSettings(0.125, 2.5, "Z", -12.5, 252.25, 60, 30) : null);
        await Assert.That(backend.Context.SourcePath).IsEqualTo(files.SourcePath);
        await Assert.That(session.Generation).IsEqualTo(0L);
        await Assert.That(await File.ReadAllTextAsync(files.SourcePath)).IsEqualTo("#usda 1.0");
    }

    [Test]
    [Arguments("metersPerUnit", "-1")]
    [Arguments("kilogramsPerUnit", "0")]
    [Arguments("upAxis", "\"X\"")]
    [Arguments("endTimeCode", "-13")]
    [Arguments("timeCodesPerSecond", "0")]
    [Arguments("framesPerSecond", "-1")]
    public async Task InvalidStageSettingsFailBeforeSessionOrBackendCreation(string field, string value)
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using McpSessionWorkspace workspace = files.CreateWorkspace(backend);
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, new OpenUsdMcpApplicationOptions(
            files.SourceRoot, files.OutputRoot, "unused", files.OutputRoot, "unused"));
        JsonNode json = JsonNode.Parse(RequestJson)!;
        json["stageSettings"]![field] = JsonNode.Parse(value);
        OpenSceneRequest request = JsonSerializer.Deserialize<OpenSceneRequest>(json.ToJsonString())!;
        await Assert.That(async () => await service.OpenSceneAsync(request, default)).Throws<ArgumentException>();
        await Assert.That(backend.Context).IsNull();
        await Assert.That(backend.Events).IsEmpty();
        await Assert.That(Directory.EnumerateFileSystemEntries(files.OutputRoot)).IsEmpty();
        await Assert.That((await workspace.GetStatusAsync()).IsActive).IsFalse();
    }
}
