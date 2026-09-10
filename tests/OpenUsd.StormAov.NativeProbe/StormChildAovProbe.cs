// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using OpenUsd;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Storm;

internal static class StormChildAovProbe
{
    internal static void Run(nint parent, string pluginPath, string stagePath, CameraState camera)
    {
        if (OpenUsdStormChildRuntime.AbiVersion != 9)
        {
            throw new InvalidOperationException("The public consumer did not load child ABI9.");
        }
        UsdStageScheduler scheduler = UsdStageScheduler.Open(stagePath);
        OpenUsdStormChildAovCapture retained;
        try
        {
            using UsdStageRenderSource source = scheduler.AcquireRenderSourceAsync().AsTask().GetAwaiter().GetResult();
            using OpenUsdStormChildSession child = OpenUsdStormChildRuntime.Create(
                parent, pluginPath, source, 64, 64, 96);
            OpenUsdStormChildDiagnostics rendered = child.Render(0, camera, 17, 23);
            for (int iteration = 0; iteration < 64 && !rendered.Converged; iteration++)
            {
                rendered = child.Render(0, camera, 17, 23);
            }
            Require(rendered.Converged, "The native reference presentation did not converge.");
            OpenUsdStormFramebufferCapture reference = child.CaptureFramebuffer(copyPixels: true);
            var request = new StormAovRequest(
                64, 64, 0, [StormAovKind.Color, StormAovKind.Depth], camera,
                callerStateRevision: 17, callerSceneRevision: 23);
            retained = Task.Run(() => child.RenderAovs(request)).GetAwaiter().GetResult();
            Require(reference.RgbaPixels.Span.SequenceEqual(retained.Framebuffer.RgbaPixels.Span),
                "Child AOV capture changed native presentation appearance or row order.");
            Require(retained.Aovs.TimeCode == 0 && retained.Aovs.CallerStateRevision == 17 &&
                retained.Aovs.CallerSceneRevision == 23 &&
                retained.NativeWorkingBytesUpperBound <= request.Limits.NativeWorkingByteLimit &&
                retained.ManagedStorageUpperBound <= request.Limits.ManagedByteLimit,
                "Child capture request binding and combined budgets.");
            StormAovOutput<float> depth = retained.Aovs.GetOutput<float>(StormAovKind.Depth);
            Require(Math.Abs(depth.GetPixel(16, 31) - 0.2f) < 0.00001f &&
                Math.Abs(depth.GetPixel(48, 31) - 0.6f) < 0.00001f && depth.GetPixel(0, 0) == 1,
                "Child capture preserves independent literal near/far/clear depth.");
            RenderJobImage nativeImage = retained.CreateJobImage(true, true);
            Require(nativeImage.RowOrder == Rgba8RowOrder.BottomUp &&
                nativeImage.Rgba.Span.SequenceEqual(reference.RgbaPixels.Span) &&
                Math.Abs(nativeImage.DeviceDepth!.Values.Span[31 * 64 + 16] - 0.2f) < 0.00001f,
                "Child disk-job conversion preserves native appearance and independent raw-plane row order.");
            child.SetSelection(new SelectionState([new SelectionItem("/World/Near")]), new Vector4(1, 1, 0, 1));
            OpenUsdStormChildAovCapture selected = child.RenderAovs(request);
            Require(!selected.Aovs.GetOutput<StormAovColor>(StormAovKind.Color).Pixels.SequenceEqual(
                    retained.Aovs.GetOutput<StormAovColor>(StormAovKind.Color).Pixels),
                "The selected child fixture must alter actual native color.");
            bool refused = false;
            try
            {
                _ = selected.Aovs.CreateJobImage(includeHdrColor: true);
            }
            catch (NotSupportedException)
            {
                refused = true;
            }
            Require(refused, "Selected native child color cannot be labeled pre-selection HDR.");
            child.SetSelection(SelectionState.Empty, Vector4.One);
            _ = child.RenderAovs(request).CreateJobImage(includeHdrColor: true);
            RequireThrows<NotSupportedException>(
                () => selected.CreateJobImage(includeHdrColor: true),
                "A later selection clear cannot certify the old selected capture.");
            RequireThrows<ArgumentException>(
                () => child.SetSelection(SelectionState.Empty, new Vector4(float.NaN, 1, 1, 1)),
                "The selection error control must be rejected.");
            RequireThrows<NotSupportedException>(
                () => child.RenderAovs(request).CreateJobImage(includeHdrColor: true),
                "A failed selection update must leave HDR qualification conservative.");
            child.SetSelection(SelectionState.Empty, Vector4.One);
            VerifyAdmissionAndRecovery(child, request, camera);
            Require(selected.Aovs.GetOutput<StormAovColor>(StormAovKind.Color).Pixels.Count == 4096,
                "Changing selection cannot mutate the captured snapshot.");
        }
        finally
        {
            scheduler.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        RenderJobImage detached = Task.Run(() => retained.CreateJobImage(true, true)).GetAwaiter().GetResult();
        Require(detached.DeviceDepth!.Values.Span[0] == 1 &&
            detached.HdrColor!.Rgba16Float.Length == 32768 &&
            retained.Framebuffer.RgbaPixels.Length == 16384,
            "Child PNG/AOV data survives renderer, source and scheduler retirement.");
        Console.WriteLine("PUBLIC_CHILD_AOV_CAPTURE=passed");
        Console.WriteLine("PUBLIC_CHILD_AOV_SELECTION_REFUSAL=passed");
        Console.WriteLine("PUBLIC_CHILD_AOV_DETACHED_OWNER=passed");
    }

    private static void VerifyAdmissionAndRecovery(
        OpenUsdStormChildSession child, StormAovRequest request, CameraState camera)
    {
        ulong before = child.GetDiagnostics().FrameCount;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        RequireThrows<OperationCanceledException>(
            () => child.RenderAovs(request, cancellation.Token),
            "Cancelled capture must refuse before native work.");
        RequireThrows<ArgumentException>(
            () => child.RenderAovs(new StormAovRequest(64, 64, 17, [StormAovKind.Depth], camera)),
            "The caller must not choose a child framebuffer.");
        RequireThrows<ArgumentOutOfRangeException>(
            () => child.RenderAovs(new StormAovRequest(64, 64, 0, [StormAovKind.Depth], camera,
                limits: new StormAovLimits(managedByteLimit: 512))),
            "The combined managed admission must include its native-presentation companion.");
        Require(child.GetDiagnostics().FrameCount == before, "Refused admission changed the native frame.");
        RequireThrows<OpenUsdStormException>(
            () => child.RenderAovs(new StormAovRequest(63, 64, 0, [StormAovKind.Depth], camera)),
            "Native capture must reject a raster that differs from the actual child viewport.");
        OpenUsdStormChildAovCapture depthOnly = child.RenderAovs(
            new StormAovRequest(64, 64, 0, [StormAovKind.Depth], camera));
        Require(depthOnly.Aovs.Outputs.Count == 1 &&
            depthOnly.CreateJobImage(includeDeviceDepth: true).DeviceDepth is not null,
            "A failed capture must release ownership and allow depth-only native presentation.");

        child.Resize(1024, 1024, 96);
        OpenUsdStormChildAovCapture maximum = child.RenderAovs(new StormAovRequest(
            1024, 1024, 0, [StormAovKind.Color, StormAovKind.Depth], camera,
            limits: new StormAovLimits(managedByteLimit: 64 * 1024 * 1024)));
        RenderJobImage image = maximum.CreateJobImage(true, true);
        Require(image.Rgba.Length == 4 * 1_048_576 &&
            image.HdrColor!.Rgba16Float.Length == 8 * 1_048_576 &&
            image.DeviceDepth!.Values.Length == 1_048_576 &&
            Math.Abs(image.DeviceDepth.Values.Span[511 * 1024 + 256] - 0.2f) < 0.00001f &&
            maximum.ManagedStorageUpperBound <= 64 * 1024 * 1024,
            "The exact child million-pixel limit must execute, not merely pass argument validation.");
        child.Resize(64, 64, 96);
        ulong generation = child.GetDiagnostics().ContextGeneration;
        child.SimulateContextLoss();
        OpenUsdStormChildAovCapture recovered = child.RenderAovs(request);
        Require(child.GetDiagnostics().ContextGeneration > generation &&
            recovered.CreateJobImage(true, true).DeviceDepth!.Values.Span[0] == 1,
            "Recreated native context must produce a fresh owned AOV capture.");
        Console.WriteLine("PUBLIC_CHILD_AOV_EXACT_LIMIT=1048576");
        Console.WriteLine("PUBLIC_CHILD_AOV_REFUSAL_RECOVERY=passed");
    }

    private static void RequireThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
