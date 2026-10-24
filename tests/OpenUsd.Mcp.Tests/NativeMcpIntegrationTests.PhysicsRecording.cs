// Copyright (c) marcschier. Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using OpenUsd.Physics.Extraction;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Physics;

namespace OpenUsd.Mcp.Tests;

public sealed partial class NativeMcpIntegrationTests
{
    [Test]
    [NotInParallel]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task NativePhysicsSequenceCapturesExactStepsAndRestoresAuthoredPreview(
        bool cancelFirst, bool fullScenePhysics)
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_PHYSICS_RECORDING_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_PHYSICS_RECORDING_REQUIRED=1 with the native CPU physics and imaging runtimes.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        NativeLayout layout = RequireNativeLayout(requireImaging: true);
        using var files = new WorkspaceTestFiles();
        string scene = $$"""
            #usda 1.0
            (
                metersPerUnit = 0.01
                upAxis = "Y"
                timeCodesPerSecond = 24
                startTimeCode = 0
                endTimeCode = 24
            )
            def PhysicsScene "Physics" {
                vector3f physics:gravityDirection = (1,0,0)
                float physics:gravityMagnitude = 2
            }
            def Camera "Camera" {
                token projection = "orthographic"
                float horizontalAperture = 400
                float verticalAperture = 400
                float2 clippingRange = (1, 10)
            }
            {{"def Cube \"Body\" (prepend apiSchemas = [\"PhysicsRigidBodyAPI\", \"Physic" +
                "sCollisionAPI\", \"PhysicsMassAPI\", \"MaterialBindingAPI\"]) {"}}
                double size = 4
                double3 xformOp:translate = (0,0,-4)
                uniform token[] xformOpOrder = ["xformOp:translate"]
                bool physics:rigidBodyEnabled = true
                bool physics:collisionEnabled = true
                float physics:mass = 1
                vector3f physics:velocity = (20,0,0)
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
        if (fullScenePhysics)
        {
            int bodyStart = scene.IndexOf("def Cube \"Body\"", StringComparison.Ordinal);
            int materialStart = scene.IndexOf("def Material \"Material\"", StringComparison.Ordinal);
            scene = scene[..bodyStart] + CompoundBodyStage + scene[materialStart..];
        }
        await File.WriteAllTextAsync(files.SourcePath, scene);
        var options = new OpenUsdMcpApplicationOptions(files.SourceRoot, files.OutputRoot,
            Path.Combine(layout.ShimRoot, "plugin", "usd"), files.OutputRoot,
            Path.Combine(files.OutputRoot, "viewer-not-launched.exe"));
        using var cancellation = new CancellationTokenSource();
        ServiceCollection collection = new();
        collection.AddOpenUsdMcpServices(options);
        if (cancelFirst)
        {
            ServiceDescriptor descriptor = collection.Single(item => item.ServiceType == typeof(
                IPreviewFrameSourceFactory));
            collection.Remove(descriptor);
            collection.AddSingleton<IPreviewFrameSourceFactory>(provider =>
                new CancelingPhysicsFactory(
                    (IPreviewFrameSourceFactory)descriptor.ImplementationFactory!(provider), cancellation));
        }
        await using ServiceProvider provider = collection.BuildServiceProvider();
        IOpenUsdMcpService service = provider.GetRequiredService<IOpenUsdMcpService>();
        IArtifactResourceStore artifacts = provider.GetRequiredService<IArtifactResourceStore>();
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        OpenUsdMcpFailureException? unitsFailure = await Assert.That(async () =>
            await service.RenderPhysicsSequenceAsync(new RenderPhysicsSequenceRequest
            {
                SessionId = session.SessionId,
                Generation = session.Generation,
                StageRevision = session.StageRevision,
                ScenarioIdentity = "wrong-units",
                ExpectedMetersPerUnit = 1,
                ExpectedUpAxis = "Y",
                FrameCount = 1,
                Width = 64,
                Height = 64
            }, default)).Throws<OpenUsdMcpFailureException>();
        await Assert.That(unitsFailure!.Code).IsEqualTo(OpenUsdMcpErrorCodes.RenderFailure);
        await Assert.That(unitsFailure.Message).Contains("metersPerUnit=0.01");
        await Assert.That(Directory.GetFiles(files.OutputRoot, "frame-*.png", SearchOption.AllDirectories)).IsEmpty();
        if (fullScenePhysics)
        {
            OpenUsdMcpFailureException? profileFailure = await Assert.That(async () =>
                await service.RenderPhysicsSequenceAsync(new RenderPhysicsSequenceRequest
                {
                    SessionId = session.SessionId,
                    Generation = session.Generation,
                    StageRevision = session.StageRevision,
                    ScenarioIdentity = "default-profile-must-not-expand",
                    ExpectedMetersPerUnit = 0.01,
                    ExpectedUpAxis = "Y",
                    FrameCount = 1,
                    Width = 64,
                    Height = 64
                }, default)).Throws<OpenUsdMcpFailureException>();
            await Assert.That(profileFailure!.Message).Contains("noninstanced");
        }
        var physicsRequest = new RenderPhysicsSequenceRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            ScenarioIdentity = "gravity-right",
            ExpectedMetersPerUnit = 0.01,
            ExpectedUpAxis = "Y",
            FullScenePhysics = fullScenePhysics,
            CameraPath = "/Camera",
            Width = 64,
            Height = 64,
            FrameCount = 3,
            StepsPerFrame = 6,
            IncludeDeviceDepth = true,
            IncludeHdrColor = true
        };
        ulong? preparedBeforeRetry = null;
        if (cancelFirst)
        {
            await Assert.That(async () =>
                await service.RenderPhysicsSequenceAsync(physicsRequest, cancellation.Token))
                .Throws<OperationCanceledException>();
            await Assert.That(Directory.GetFiles(files.OutputRoot, "frame-*.png",
                SearchOption.AllDirectories)).IsEmpty();
            preparedBeforeRetry = UsdPhysicsStageExtractor.GetTraversalCount();
        }
        McpRenderSequenceResultDto job = await service.RenderPhysicsSequenceAsync(physicsRequest, default);
        if (preparedBeforeRetry is { } traversals)
        {
            await Assert.That(UsdPhysicsStageExtractor.GetTraversalCount()).IsEqualTo(traversals)
                .Because(
                    "A render retry reuses the complete source-revision-matched page, not its old simulation state.");
        }
        await Assert.That(job.Simulation).IsNotNull();
        await Assert.That(job.Simulation!.FirstStep).IsEqualTo(6ul);
        await Assert.That(job.Simulation.LastStep).IsEqualTo(18ul);
        await Assert.That(job.Simulation.MetersPerUnit).IsEqualTo(0.01);
        await Assert.That(job.Simulation.UpAxis).IsEqualTo("Y");
        await Assert.That(job.Simulation.FullScenePhysics).IsEqualTo(fullScenePhysics);
        var centers = new double[3];
        for (int index = 0; index < centers.Length; index++)
        {
            McpSequenceFrameResultDto frame = await service.ReadSequenceFrameAsync(
                new ReadSequenceFrameRequest { JobId = job.JobId, FrameIndex = index }, default);
            await Assert.That(frame.Simulation!.StepIndex).IsEqualTo((ulong)(index + 1) * 6);
            await Assert.That(frame.Simulation.Epoch).IsEqualTo(job.Simulation.Epoch);
            await Assert.That(frame.TimeCode).IsEqualTo((double)(index + 1) * 6);
            await Assert.That(frame.Simulation.BodyCount).IsEqualTo(1);
            await Assert.That(frame.DeviceDepth).IsNotNull();
            await Assert.That(frame.HdrColor).IsNotNull();
            ArtifactResourceContent? png = await artifacts.ReadAsync(new Uri(frame.Artifact.Uri));
            (centers[index], int coverage) = RedCenter(PngRgba8Decoder.Decode(png!.Content.Span));
            await Assert.That(coverage).IsGreaterThan(20);
        }
        await Assert.That(centers[1]).IsGreaterThan(centers[0] + 2);
        await Assert.That(centers[2]).IsGreaterThan(centers[1] + 2);
        McpSequenceSheetResultDto sheet = await service.ReadSequenceSheetAsync(new ReadSequenceSheetRequest
        {
            JobId = job.JobId,
            Width = 192,
            Height = 64
        }, default);
        await Assert.That(sheet.Tiles.Count).IsEqualTo(3);
        if (Environment.GetEnvironmentVariable("OPENUSD_MCP_PHYSICS_EVIDENCE_ROOT") is { Length: > 0 } evidenceRoot)
        {
            string destination = Path.Combine(evidenceRoot,
                (fullScenePhysics ? "compound-" : string.Empty) + (cancelFirst ? "recovery" : "normal"));
            if (Directory.Exists(destination))
            {
                throw new IOException("Preserve existing physics recording evidence.");
            }
            Directory.CreateDirectory(destination);
            await File.WriteAllTextAsync(Path.Combine(destination, "scene.usda"), scene);
            string manifest = Path.Combine(files.OutputRoot, job.ManifestPath);
            File.Copy(manifest, Path.Combine(destination, "manifest.json"));
            for (int index = 0; index < 3; index++)
            {
                File.Copy(Path.Combine(Path.GetDirectoryName(manifest)!, $"frame-{index:D6}.png"),
                    Path.Combine(destination, $"frame-{index:D6}.png"));
            }
            ArtifactResourceContent? contact = await artifacts.ReadAsync(new Uri(sheet.Artifact.Uri));
            await File.WriteAllBytesAsync(Path.Combine(destination, "contact-sheet.png"), contact!.Content.ToArray());
        }
        McpRenderSequenceResultDto restored = await service.RenderSequenceAsync(new RenderSequenceRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            Width = 64,
            Height = 64,
            CameraPath = "/Camera",
            FrameCount = 1
        }, default);
        McpSequenceFrameResultDto original = await service.ReadSequenceFrameAsync(
            new ReadSequenceFrameRequest { JobId = restored.JobId, FrameIndex = 0 }, default);
        ArtifactResourceContent? originalPng = await artifacts.ReadAsync(new Uri(original.Artifact.Uri));
        (double originalCenter, _) = RedCenter(PngRgba8Decoder.Decode(originalPng!.Content.Span));
        await Assert.That(originalCenter).IsLessThan(centers[0]);
        await Assert.That(original.Simulation).IsNull();
        await Assert.That(await File.ReadAllTextAsync(files.SourcePath)).IsEqualTo(scene);
        McpSessionDto unchanged = await service.GetSceneAsync(new SceneRevisionRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision
        }, default);
        await Assert.That(unchanged.StageRevision).IsEqualTo(session.StageRevision);
        ulong beforeEdit = UsdPhysicsStageExtractor.GetTraversalCount();
        McpEditResultDto edited = await service.ApplyEditsAsync(new ApplyEditsRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            Edits =
            [
                new WorkspaceEditDto
                {
                    Kind = "set_bool",
                    PrimPath = "/Body",
                    AttributeName = "physics:kinematicEnabled",
                    BoolValue = true
                }
            ]
        }, default);
        McpRenderSequenceResultDto changed = await service.RenderPhysicsSequenceAsync(new RenderPhysicsSequenceRequest
        {
            SessionId = session.SessionId,
            Generation = edited.Generation,
            StageRevision = edited.StageRevision,
            ScenarioIdentity = "changed-kinematic-mode",
            ExpectedMetersPerUnit = 0.01,
            ExpectedUpAxis = "Y",
            FullScenePhysics = fullScenePhysics,
            CameraPath = "/Camera",
            Width = 64,
            Height = 64,
            FrameCount = 1,
            StepsPerFrame = 6
        }, default);
        await Assert.That(UsdPhysicsStageExtractor.GetTraversalCount()).IsGreaterThan(beforeEdit);
        McpSequenceFrameResultDto changedFrame = await service.ReadSequenceFrameAsync(
            new ReadSequenceFrameRequest { JobId = changed.JobId, FrameIndex = 0 }, default);
        ArtifactResourceContent? changedPng = await artifacts.ReadAsync(new Uri(changedFrame.Artifact.Uri));
        (double changedCenter, _) = RedCenter(PngRgba8Decoder.Decode(changedPng!.Content.Span));
        await Assert.That(changedCenter).IsEqualTo(originalCenter).Within(1);
        await Assert.That(await File.ReadAllTextAsync(files.SourcePath)).IsEqualTo(scene);
    }

    private const string CompoundBodyStage = """
        def Xform "Body" (prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsMassAPI", "MaterialBindingAPI"]) {
            double3 xformOp:translate = (0,0,-4)
            uniform token[] xformOpOrder = ["xformOp:translate"]
            float physics:mass = 1
            vector3f physics:velocity = (20,0,0)
            rel material:binding = </Material>
            def Cube "Left" (prepend apiSchemas = ["PhysicsCollisionAPI"]) {
                double size = 2.5
                double3 xformOp:translate = (-1.5,0,0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            def Cube "Right" (prepend apiSchemas = ["PhysicsCollisionAPI"]) {
                double size = 2.5
                double3 xformOp:translate = (1.5,0,0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
        }
        def Xform "StaticPrototype" {
            def Cube "Collider" (prepend apiSchemas = ["PhysicsCollisionAPI"]) {
                double3 xformOp:translate = (100,0,0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
        }
        def Xform "StaticInstance" (references = </StaticPrototype>; instanceable = true) {
            double3 xformOp:translate = (200,0,0)
            uniform token[] xformOpOrder = ["xformOp:translate"]
        }

        """;

    private sealed class CancelingPhysicsFactory(
        IPreviewFrameSourceFactory inner, CancellationTokenSource cancellation) : IPreviewFrameSourceFactory
    {
        public IPreviewFrameSource Create(PreviewCaptureRequest request,
            CancellationToken cancellationToken = default) =>
            new CancelingPhysicsSource(inner.Create(request, cancellationToken), cancellation);
    }

    private sealed class CancelingPhysicsSource(
        IPreviewFrameSource inner, CancellationTokenSource cancellation)
        : IPreviewFrameSource, IPhysicsRenderRecordingSource
    {
        public ImageRgba8 Capture(CaptureView view, int width, int height) => inner.Capture(view, width, height);
        public void Dispose() => inner.Dispose();
        public void Validate(RenderDiskJobRequest request, PhysicsRenderRecordingContext context,
            CancellationToken cancellationToken) =>
            ((IPhysicsRenderRecordingSource)inner).Validate(request, context, cancellationToken);
        public PhysicsRenderCaptureResult Capture(StageRenderState state, PhysicsRenderRecordingFrame frame,
            CancellationToken cancellationToken)
        {
            PhysicsRenderCaptureResult result = ((IPhysicsRenderRecordingSource)inner).Capture(state, frame,
                cancellationToken);
            cancellation.Cancel();
            return result;
        }
        public void ClearOverrides() => ((IPhysicsRenderRecordingSource)inner).ClearOverrides();
    }
}
