// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenUsd.Physics;
using OpenUsd.Physics.Extraction;

namespace OpenUsd.Mcp.Tests;

public sealed partial class NativeMcpIntegrationTests
{
    [Test]
    [NotInParallel]
    public async Task NativeExplicitStageSettingsDriveExactPhysicsTimeAndReset()
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_PHYSICS_RECORDING_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_PHYSICS_RECORDING_REQUIRED=1 with the matching native CPU physics runtime.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        _ = RequireNativeLayout(requireImaging: false);
        using var files = new WorkspaceTestFiles();
        const string scene = """
            #usda 1.0
            (
                metersPerUnit = 1
                kilogramsPerUnit = 1
                upAxis = "Z"
                startTimeCode = 12
                endTimeCode = 252
                timeCodesPerSecond = 60
                framesPerSecond = 30
            )
            def PhysicsScene "Physics" {
                vector3f physics:gravityDirection = (0, 0, -1)
                float physics:gravityMagnitude = 9.81
            }
            def Cube "Body" (prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsCollisionAPI", "PhysicsMassAPI"]) {
                double size = 0.5
                double3 xformOp:translate = (0, 0, 4)
                uniform token[] xformOpOrder = ["xformOp:translate"]
                float physics:mass = 1
                vector3f physics:velocity = (0, 0, 2)
            }
            """;
        await File.WriteAllTextAsync(files.SourcePath, scene);
        await using var workspace = new McpSessionWorkspace(
            new McpSessionWorkspaceOptions(files.SourceRoot, files.OutputRoot));
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda",
            new WorkspaceStageSettings(1, 1, "Z", 12, 252, 60, 30), default);
        var revision = new WorkspaceSessionRevision(session.SessionId, session.Generation, session.StageRevision);
        await Assert.That(async () => await workspace.PreparePhysicsRecordingAsync(revision, 0.01, "Y"))
            .Throws<NotSupportedException>();
        await using WorkspacePhysicsRecording recording = await workspace.PreparePhysicsRecordingAsync(
            revision, 1, "Z", fullScenePhysics: true, default);
        await Assert.That(recording.Transport.Timeline).IsEqualTo(new UsdPhysicsTimeline(60, 12, 252));
        await Assert.That(recording.Transport.FixedStep.Seconds).IsEqualTo(1d / 60);
        await Assert.That(recording.Transport.FixedStep.WasClamped).IsFalse();
        using (UsdPhysicsFrameLease captured = await recording.Transport.StepAndAcquireFrameAsync(6))
        {
            UsdPhysicsFrame frame = captured.Frame;
            await Assert.That(frame.StepIndex).IsEqualTo(6ul);
            await Assert.That(frame.TimeCode).IsEqualTo(18d);
            await Assert.That(frame.SimulationSeconds).IsEqualTo(0.1).Within(1e-12);
            await Assert.That(frame.BodyCount).IsEqualTo(1);
            await Assert.That(frame.Bodies[0].Position.Z).IsGreaterThan(4);
        }
        await recording.Transport.ResetAsync();
        await Assert.That(recording.Transport.TryAcquireLatestFrame(out UsdPhysicsFrameLease reset)).IsTrue();
        using (reset)
        {
            await Assert.That(reset.Frame.StepIndex).IsEqualTo(0ul);
            await Assert.That(reset.Frame.TimeCode).IsEqualTo(12d);
            await Assert.That(reset.Frame.BodyCount).IsEqualTo(1);
            await Assert.That(reset.Frame.Bodies[0].Position.Z).IsEqualTo(4d);
        }
        await Assert.That((await workspace.GetStatusAsync()).Session!.StageRevision).IsEqualTo(session.StageRevision);
        await Assert.That(await File.ReadAllTextAsync(files.SourcePath)).IsEqualTo(scene);
    }

    [Test]
    [NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeExplicitStageSettingsSurviveExportAndRollbackWithoutChangingSource(bool configure)
    {
        _ = RequireNativeLayout(requireImaging: false);
        using var files = new WorkspaceTestFiles();
        const string source = """
            #usda 1.0
            (
                metersPerUnit = 1
                kilogramsPerUnit = 2
                upAxis = "Z"
                startTimeCode = 12
                endTimeCode = 252
                timeCodesPerSecond = 60
                framesPerSecond = 30
            )
            def Xform "World" {}
            """;
        await File.WriteAllTextAsync(files.SourcePath, source);
        const string configuredRequest = """
            {
                "sourcePath": "scene.usda",
                "stageSettings": {
                    "metersPerUnit": 1,
                    "kilogramsPerUnit": 2,
                    "upAxis": "Z",
                    "startTimeCode": 12,
                    "endTimeCode": 252,
                    "timeCodesPerSecond": 60,
                    "framesPerSecond": 30
                }
            }
            """;
        OpenSceneRequest request = JsonSerializer.Deserialize<OpenSceneRequest>(
            configure ? configuredRequest : """{"sourcePath":"scene.usda"}""")!;
        var options = new OpenUsdMcpApplicationOptions(
            files.SourceRoot, files.OutputRoot, "unused", files.OutputRoot,
            Path.Combine(files.OutputRoot, "viewer-not-launched.exe"));
        await using ServiceProvider provider = new ServiceCollection()
            .AddOpenUsdMcpServices(options).BuildServiceProvider();
        IOpenUsdMcpService service = provider.GetRequiredService<IOpenUsdMcpService>();
        McpSessionWorkspace workspace = provider.GetRequiredService<McpSessionWorkspace>();
        McpSessionDto session = await service.OpenSceneAsync(request, default);
        var revision = new WorkspaceSessionRevision(session.SessionId, session.Generation, session.StageRevision);
        WorkspaceFinalStageResult initial = await workspace.ExportFinalStageAsync(revision, "initial.usda");
        await RequireStageSettingsAsync(initial.FinalStagePath, configure);
        byte[] overlay = await File.ReadAllBytesAsync(initial.Snapshot.Session.OverlayPath);

        WorkspaceCheckpointResult checkpoint = await workspace.CheckpointAsync(revision);
        WorkspaceEditResult edit = await workspace.EditAsync(revision,
            new WorkspaceEditBatch([new DefinePrimWorkspaceEdit("/Temporary", "Xform")]));
        WorkspaceRollbackResult rollback = await workspace.RollbackAsync(
            new WorkspaceSessionRevision(edit.SessionId, edit.Generation, edit.StageRevision),
            checkpoint.Checkpoint.CheckpointId);
        WorkspaceFinalStageResult restored = await workspace.ExportFinalStageAsync(
            new WorkspaceSessionRevision(rollback.SessionId, rollback.Generation, rollback.StageRevision),
            "restored.usda");
        await RequireStageSettingsAsync(restored.FinalStagePath, configure);
        await Assert.That((await File.ReadAllBytesAsync(restored.Snapshot.Session.OverlayPath)).SequenceEqual(overlay))
            .IsTrue();
        await using (UsdStageScheduler scheduler = UsdStageScheduler.Open(restored.FinalStagePath))
        {
            (bool temporary, bool world) = await scheduler.InvokeAsync(
                static stage => (stage.HasPrim("/Temporary"), stage.HasPrim("/World")));
            await Assert.That(temporary).IsFalse();
            await Assert.That(world).IsTrue();
        }
        await Assert.That(await File.ReadAllTextAsync(files.SourcePath)).IsEqualTo(source);
    }

    private static async Task RequireStageSettingsAsync(string path, bool configured)
    {
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(path);
        var values = await scheduler.InvokeAsync(static stage =>
        {
            UsdPhysicsExtractionPage metadata = UsdPhysicsStageExtractor.Extract(
                stage, new UsdPhysicsExtractionOptions { IncludeMeshData = false });
            return (metadata.MetersPerUnit, metadata.KilogramsPerUnit, metadata.UpAxis,
                stage.StartTimeCode, stage.EndTimeCode, stage.TimeCodesPerSecond, stage.FramesPerSecond);
        });
        await Assert.That(values.MetersPerUnit).IsEqualTo(configured ? 1 : 0.01);
        await Assert.That(values.KilogramsPerUnit).IsEqualTo(configured ? 2 : 1);
        await Assert.That(values.UpAxis).IsEqualTo(
            configured ? UsdPhysicsExtractionUpAxis.Z : UsdPhysicsExtractionUpAxis.Y);
        if (configured)
        {
            await Assert.That(values.StartTimeCode).IsEqualTo(12d);
            await Assert.That(values.EndTimeCode).IsEqualTo(252d);
            await Assert.That(values.TimeCodesPerSecond).IsEqualTo(60d);
            await Assert.That(values.FramesPerSecond).IsEqualTo(30d);
        }
    }
}
