// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;
using System.Numerics;
using System.Buffers.Binary;

namespace OpenUsd.Rendering.Tests;

public sealed class RenderDiskJobTests
{
    [Test]
    [Arguments("missing")]
    [Arguments("unexpected")]
    [Arguments("dimensions")]
    [Arguments("nonfinite")]
    [Arguments("range")]
    public async Task InvalidDepthPlanesCannotProduceACompletedJob(string failure)
    {
        string root = Directory.CreateTempSubdirectory("openusd-job-depth-failure-").FullName;
        StageRenderState state = StageRenderState.Create(new StageIdentity("source.usda"))
            .WithViewport(new ViewportDimensions(2, 1));
        float[] values = failure switch
        {
            "nonfinite" => [float.NaN, 1],
            "range" => [2, 1],
            "dimensions" => [0.2f],
            _ => [0.2f, 1]
        };
        var source = new CallbackSource(_ => new RenderJobImage(
            2, 1, new byte[] { 255, 0, 0, 255, 0, 255, 0, 255 }, Rgba8RowOrder.TopDown)
        {
            DeviceDepth = failure == "missing" ? null : new RenderJobDeviceDepth(values.Length, 1, values)
        });
        try
        {
            var request = new RenderDiskJobRequest(
                Path.Combine(root, "sequence"), [state], includeDeviceDepth: failure != "unexpected");
            if (failure is "missing" or "unexpected")
            {
                await Assert.That(() => RenderDiskJob.Execute(request, source)).Throws<NotSupportedException>();
            }
            else
            {
                await Assert.That(() => RenderDiskJob.Execute(request, source)).Throws<InvalidDataException>();
            }
            await Assert.That(Directory.GetFileSystemEntries(root)).IsEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task DepthAdmissionChargesBothOutputsAndStagingBeforeRendering()
    {
        string destination = Path.Combine(Path.GetTempPath(), $"depth-budget-{Guid.NewGuid():N}");
        StageRenderState state = StageRenderState.Create(new StageIdentity("source.usda"))
            .WithViewport(new ViewportDimensions(2049, 2048));
        await Assert.That(() => new RenderDiskJobRequest(destination, [state], includeDeviceDepth: true))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(Directory.Exists(destination)).IsFalse();
    }

    [Test]
    public async Task DepthAdmissionAlsoIncludesTheSelectionUploadCopy()
    {
        string destination = Path.Combine(Path.GetTempPath(), $"depth-upload-budget-{Guid.NewGuid():N}");
        StageRenderState state = StageRenderState.Create(new StageIdentity("source.usda"))
            .WithViewport(new ViewportDimensions(2, 1));
        await Assert.That(() => new RenderDiskJobRequest(
            destination, [state], includeDeviceDepth: true, new RenderDiskJobLimits(maximumFrameBytes: 39)))
            .Throws<ArgumentOutOfRangeException>();
        var exact = new RenderDiskJobRequest(
            destination, [state], includeDeviceDepth: true, new RenderDiskJobLimits(maximumFrameBytes: 40));
        await Assert.That(exact.Frames.Count).IsEqualTo(1);
        await Assert.That(Directory.Exists(destination)).IsFalse();
    }

    [Test]
    public async Task RequestedDeviceDepthIsStoredAsExactFloatDataWithAnExplicitConvention()
    {
        string root = Directory.CreateTempSubdirectory("openusd-job-depth-").FullName;
        StageRenderState state = StageRenderState.Create(new StageIdentity("source.usda"))
            .WithViewport(new ViewportDimensions(2, 1));
        float[] depthSamples = [0.25f, 1f];
        var source = new CallbackSource(_ => new RenderJobImage(
            2, 1, new byte[] { 255, 0, 0, 255, 0, 255, 0, 255 }, Rgba8RowOrder.TopDown)
        {
            DeviceDepth = new RenderJobDeviceDepth(2, 1, depthSamples)
        });
        try
        {
            RenderDiskJobResult result = RenderDiskJob.Execute(new RenderDiskJobRequest(
                Path.Combine(root, "sequence"), [state], includeDeviceDepth: true), source);
            RenderDiskFrameResult frame = result.Frames.Single();
            byte[] depth = await File.ReadAllBytesAsync(Path.Combine(result.OutputDirectory, frame.DepthFileName!));
            await Assert.That(Convert.ToHexString(depth)).IsEqualTo("0000803E0000803F");
            await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(depth)).IsEqualTo(0.25f);
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(
                Path.Combine(result.OutputDirectory, "manifest.json")));
            JsonElement metadata = manifest.RootElement.GetProperty("frames")[0].GetProperty("deviceDepth");
            await Assert.That(metadata.GetProperty("convention").GetString())
                .IsEqualTo("normalized-device-depth-zero-to-one");
            await Assert.That(metadata.GetProperty("rowOrder").GetString()).IsEqualTo("top-down");
            await Assert.That(metadata.GetProperty("clearValue").GetSingle()).IsEqualTo(1f);
            await Assert.That(result.TotalBytes).IsEqualTo(
                Directory.GetFiles(result.OutputDirectory).Sum(static path => new FileInfo(path).Length));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SelectionContextsCannotMutateAdmittedOrCompletedFrameState(bool nested)
    {
        string root = Directory.CreateTempSubdirectory("openusd-job-selection-").FullName;
        SelectionItem item = nested
            ? SelectionItem.FromInstancerContext("/Prototype",
                [new("/Outer", 1), new("/Instances", 3)], 4, SelectionElementKind.Face)
            : new SelectionItem("/Prototype", "/Instances", 3);
        StageRenderState state = StageRenderState.Create(new StageIdentity("source.usda"))
            .WithViewport(new ViewportDimensions(2, 1)).WithSelection(new SelectionState([item]));
        var request = new RenderDiskJobRequest(Path.Combine(root, "sequence"), [state]);
        try
        {
            IReadOnlyList<SelectionInstancerEntry> exposed = item.InstancerContext;
            await Assert.That(exposed is System.Collections.ICollection).IsFalse()
                .Because("ICollection.SyncRoot must not provide a route to the mutable backing array");
            await Assert.That(() => ((IList<SelectionInstancerEntry>)exposed)[^1] = new("/Changed", 99))
                .Throws<InvalidCastException>();
            await Assert.That(request.Frames[0].Selection.Items[0].InstancerPath).IsEqualTo("/Instances");
            RenderDiskJobResult result = RenderDiskJob.Execute(request, new TwoFrameSource());
            IReadOnlyList<SelectionInstancerEntry> completed =
                result.Frames[0].State.Selection.Items[0].InstancerContext;
            await Assert.That(completed is System.Collections.ICollection).IsFalse();
            await Assert.That(() => ((IList<SelectionInstancerEntry>)completed)[^1] =
                new SelectionInstancerEntry("/" + new string('x', 1025), 99)).Throws<InvalidCastException>();
            await Assert.That(result.Frames[0].State.Selection.Items[0].InstanceIndex).IsEqualTo(3);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task JobDiagnosticsRetainEarlierFrameDegradationWhenTheLastFrameIsClean()
    {
        string root = Directory.CreateTempSubdirectory("openusd-render-job-diagnostics-").FullName;
        StageRenderState state = StageRenderState.Create(new StageIdentity("source.usda"))
            .WithViewport(new ViewportDimensions(1, 1));
        var source = new CallbackSource(frame => new RenderJobImage(
            1, 1, new byte[] { 1, 2, 3, 255 }, Rgba8RowOrder.TopDown)
        {
            Diagnostics = frame.Time.TimeCode == 0
                ? new RenderDiagnosticsState([new RenderDiagnostic(
                    RenderDiagnosticSeverity.Warning, "texture.missing", "A source texture was missing.")])
                : RenderDiagnosticsState.Empty
        });
        try
        {
            RenderDiskJobResult result = RenderDiskJob.Execute(new RenderDiskJobRequest(
                Path.Combine(root, "sequence"), [state, state.WithTime(new StageTime(1))]), source);
            await Assert.That(result.Diagnostics.Single().Code).IsEqualTo("texture.missing");
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(
                Path.Combine(result.OutputDirectory, "manifest.json")));
            await Assert.That(manifest.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString())
                .IsEqualTo("texture.missing");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task JobPublicationSupportsLongDescendantPaths()
    {
        string root = Directory.CreateTempSubdirectory("openusd-render-job-long-").FullName;
        string parent = Path.Combine(root, new string('p', Math.Max(1, 197 - root.Length - 1)));
        StageRenderState state = StageRenderState.Create(new StageIdentity("source.usda"))
            .WithViewport(new ViewportDimensions(2, 1));
        try
        {
            Directory.CreateDirectory(parent);
            RenderDiskJobResult result = RenderDiskJob.Execute(
                new RenderDiskJobRequest(Path.Combine(parent, "sequence"), [state]), new TwoFrameSource());
            await Assert.That(File.Exists(Path.Combine(result.OutputDirectory, "manifest.json"))).IsTrue();
            await Assert.That(Directory.GetDirectories(parent).Single()).IsEqualTo(result.OutputDirectory);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ManifestRetainsTheExactCameraDisplayAndColorRequest()
    {
        string root = Directory.CreateTempSubdirectory("openusd-render-job-state-").FullName;
        var settings = new RenderSettings(4, false, true, new Vector4(0.1f, 0.2f, 0.3f, 0.4f),
            false, true, RenderComplexity.Medium, RenderOutputTransform.Identity, 2)
        {
            DisplayTransform = new RenderDisplayTransform(
                Path.Combine(root, "config.ocio"), "linear", "Display", "View", "Look")
        };
        StageRenderState state = StageRenderState.Create(new StageIdentity("source.usda"))
            .WithViewport(new ViewportDimensions(2, 1))
            .WithCamera(new CameraState(Matrix4x4.CreateTranslation(3, 4, 5), Matrix4x4.Identity))
            .WithDisplay(new SceneDisplayState(
                RenderPurpose.Guide, RenderVisibility.RespectAuthored, RenderDrawMode.Wireframe))
            .WithRenderSettings(settings);
        StageRenderState? observed = null;
        var source = new CallbackSource(requested =>
        {
            observed = requested;
            return new RenderJobImage(2, 1, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, Rgba8RowOrder.TopDown);
        });
        try
        {
            RenderDiskJobResult result = RenderDiskJob.Execute(
                new RenderDiskJobRequest(Path.Combine(root, "sequence"), [state]), source);
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(
                Path.Combine(result.OutputDirectory, "manifest.json")));
            JsonElement frame = manifest.RootElement.GetProperty("frames")[0];
            await Assert.That(observed).IsSameReferenceAs(state);
            await Assert.That(frame.GetProperty("camera").GetProperty("view")[12].GetSingle()).IsEqualTo(3f);
            await Assert.That(frame.GetProperty("camera").GetProperty("view")[14].GetSingle()).IsEqualTo(5f);
            await Assert.That(frame.GetProperty("display").GetProperty("drawMode").GetString()).IsEqualTo("Wireframe");
            await Assert.That(frame.GetProperty("settings").GetProperty("samplesPerPixel").GetInt32()).IsEqualTo(4);
            await Assert.That(frame.GetProperty("settings").GetProperty("exposure").GetSingle()).IsEqualTo(2f);
            await Assert.That(frame.GetProperty("settings").GetProperty("clearColor")[3].GetSingle()).IsEqualTo(0.4f);
            await Assert.That(frame.GetProperty("settings").GetProperty("displayTransform")
                .GetProperty("view").GetString()).IsEqualTo("View");
            await Assert.That(File.Exists(Path.Combine(root, "config.ocio"))).IsFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments("source")]
    [Arguments("cancellation")]
    [Arguments("image-quota")]
    [Arguments("manifest-quota")]
    [Arguments("dimensions")]
    public async Task FailedJobsNeverPublishPartialOutputOrKeepStaging(string failure)
    {
        string root = Directory.CreateTempSubdirectory("openusd-render-job-failure-").FullName;
        string destination = Path.Combine(root, "sequence");
        StageRenderState initial = StageRenderState.Create(new StageIdentity("source.usda"))
            .WithViewport(new ViewportDimensions(2, 1));
        using var cancellation = new CancellationTokenSource();
        var source = new CallbackSource(state =>
        {
            if (state.Time.TimeCode == 2)
            {
                if (Directory.Exists(destination))
                {
                    throw new IOException("The job published before its last frame completed.");
                }
                if (failure == "source")
                {
                    throw new InvalidOperationException("Renderer failure.");
                }
                if (failure == "cancellation")
                {
                    cancellation.Cancel();
                }
            }
            return failure == "dimensions"
                ? new RenderJobImage(1, 1, new byte[] { 1, 2, 3, 4 }, Rgba8RowOrder.TopDown)
                : new RenderJobImage(2, 1, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, Rgba8RowOrder.TopDown);
        });
        var limits = failure switch
        {
            "image-quota" => new RenderDiskJobLimits(maximumFrameBytes: 32),
            "manifest-quota" => new RenderDiskJobLimits(maximumTotalBytes: 200),
            _ => RenderDiskJobLimits.Default
        };
        try
        {
            var request = new RenderDiskJobRequest(destination,
                [initial, initial.WithTime(new StageTime(2))], limits);
            if (failure == "cancellation")
            {
                await Assert.That(() => RenderDiskJob.Execute(request, source, cancellation.Token))
                    .Throws<OperationCanceledException>();
            }
            else if (failure == "dimensions")
            {
                await Assert.That(() => RenderDiskJob.Execute(request, source))
                    .Throws<InvalidDataException>();
            }
            else
            {
                await Assert.That(() => RenderDiskJob.Execute(request, source))
                    .Throws<InvalidOperationException>();
            }
            await Assert.That(Directory.GetFileSystemEntries(root)).IsEmpty();
            if (failure == "manifest-quota")
            {
                await Assert.That(source.Calls).IsEqualTo(2);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ExistingOutputAndCallerMutationCannotChangeAnAdmittedJob()
    {
        string root = Directory.CreateTempSubdirectory("openusd-render-job-existing-").FullName;
        string destination = Path.Combine(root, "sequence");
        StageRenderState initial = StageRenderState.Create(new StageIdentity("source.usda"))
            .WithViewport(new ViewportDimensions(2, 1));
        StageRenderState[] callerFrames = [initial];
        var request = new RenderDiskJobRequest(destination, callerFrames);
        callerFrames[0] = initial.WithTime(new StageTime(99));
        var source = new TwoFrameSource();
        try
        {
            Directory.CreateDirectory(destination);
            string previous = Path.Combine(destination, "original.txt");
            await File.WriteAllTextAsync(previous, "keep");
            await Assert.That(() => RenderDiskJob.Execute(request, source)).Throws<IOException>();
            await Assert.That(source.ObservedTimes).IsEmpty();
            await Assert.That(await File.ReadAllTextAsync(previous)).IsEqualTo("keep");
            await Assert.That(request.Frames[0].Time.TimeCode).IsEqualTo(0d);
            await Assert.That(request.Frames is System.Collections.ICollection).IsFalse();
            await Assert.That(() => ((IList<StageRenderState>)request.Frames)[0] = callerFrames[0])
                .Throws<InvalidCastException>();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ASequencePublishesOrderedFramesAndAManifestWithoutRetainingPixelBuffers()
    {
        string root = Directory.CreateTempSubdirectory("openusd-render-job-").FullName;
        string destination = Path.Combine(root, "sequence");
        StageRenderState initial = StageRenderState.Create(new StageIdentity("source.usda"))
            .WithViewport(new ViewportDimensions(2, 1))
            .WithRenderSettings(RenderSettings.PresentationDefault);
        StageRenderState[] frames = [initial.WithTime(new StageTime(0)), initial.WithTime(new StageTime(2))];
        var source = new TwoFrameSource();
        try
        {
            RenderDiskJobResult result = RenderDiskJob.Execute(
                new RenderDiskJobRequest(destination, frames), source);

            await Assert.That(source.ObservedTimes).IsEquivalentTo([0d, 2d]);
            await Assert.That(source.ObservedTimes[0]).IsEqualTo(0d);
            await Assert.That(result.OutputDirectory).IsEqualTo(destination);
            await Assert.That(result.Frames.Count).IsEqualTo(2);
            await Assert.That(result.Frames is System.Collections.ICollection).IsFalse();
            await Assert.That(result.Diagnostics is System.Collections.ICollection).IsFalse();
            await Assert.That(result.Frames[0].FileName).IsEqualTo("frame-000000.png");
            await Assert.That(result.Frames[1].FileName).IsEqualTo("frame-000001.png");
            PngRgba8Image first = PngRgba8Reader.Decode(await File.ReadAllBytesAsync(
                Path.Combine(destination, result.Frames[0].FileName)));
            PngRgba8Image second = PngRgba8Reader.Decode(await File.ReadAllBytesAsync(
                Path.Combine(destination, result.Frames[1].FileName)));
            await Assert.That(Convert.ToHexString(first.Pixels)).IsEqualTo("FF0000FF00FF0080");
            await Assert.That(Convert.ToHexString(second.Pixels)).IsEqualTo("0000FF400A141E28");
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(
                Path.Combine(destination, "manifest.json")));
            await Assert.That(manifest.RootElement.GetProperty("schemaVersion").GetInt32()).IsEqualTo(1);
            await Assert.That(manifest.RootElement.GetProperty("frames")[1].GetProperty("timeCode").GetDouble())
                .IsEqualTo(2d);
            await Assert.That(Directory.GetDirectories(root).Single()).IsEqualTo(destination);
            await Assert.That(Directory.GetFiles(destination).Length).IsEqualTo(3);
            await Assert.That(result.TotalBytes).IsEqualTo(
                Directory.GetFiles(destination).Sum(static path => new FileInfo(path).Length));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TwoFrameSource : IRenderJobFrameSource
    {
        internal List<double> ObservedTimes { get; } = [];

        public RenderJobImage Render(StageRenderState state, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObservedTimes.Add(state.Time.TimeCode);
            byte[] pixels = ObservedTimes.Count == 1
                ? [255, 0, 0, 255, 0, 255, 0, 128]
                : [0, 0, 255, 64, 10, 20, 30, 40];
            return new RenderJobImage(2, 1, pixels, Rgba8RowOrder.TopDown);
        }
    }

    private sealed class CallbackSource(Func<StageRenderState, RenderJobImage> render) : IRenderJobFrameSource
    {
        internal int Calls { get; private set; }

        public RenderJobImage Render(StageRenderState state, CancellationToken cancellationToken)
        {
            Calls++;
            return render(state);
        }
    }
}
