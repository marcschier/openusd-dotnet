// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Storm;

if (!OperatingSystem.IsWindows())
{
    throw new PlatformNotSupportedException("This proof requires hidden Windows WGL.");
}
if (args.Length is not (0 or 2))
{
    throw new ArgumentException("Usage: probe [<matched-runtime-root> <planar-usda>]");
}
bool packageOnly = args.Length == 0;
string runtime = packageOnly ? AppContext.BaseDirectory : Path.GetFullPath(args[0]);
string stagePath = packageOnly ? Path.Combine(runtime, "storm_aov_planes.usda") : args[1];
string libraryPath = Path.Combine(runtime, packageOnly ? string.Empty : "bin", "openusd_hydra.dll");
string pluginPath = Path.Combine(runtime, packageOnly ? "usd" : Path.Combine("plugin", "usd"));
if (packageOnly)
{
    string systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    Environment.SetEnvironmentVariable("PATH", runtime + ";" + Path.Combine(systemRoot, "System32"));
    Environment.SetEnvironmentVariable("PXR_PLUGINPATH_NAME", pluginPath);
}
NativeLibrary.SetDllImportResolver(typeof(OpenUsdStormRuntime).Assembly, (name, _, _) => name switch
{
    "openusd_hydra" => NativeLibrary.Load(libraryPath),
    "openusd_storm_child" => NativeLibrary.Load(
        Path.Combine(runtime, packageOnly ? string.Empty : "bin", "openusd_storm_child.dll")),
    _ => 0
});
using var context = new WindowsContext();
Console.WriteLine($"PUBLIC_AOV_DRIVER={WindowsContext.Driver}");
if (OpenUsdStormRuntime.AbiVersion != 9)
{
    throw new InvalidOperationException("The public consumer did not load Storm ABI9.");
}
StormAovSnapshot retained;
var camera = new CameraState(
    Matrix4x4.Identity,
    new Matrix4x4(
        0.25f, 0, 0, 0, 0, 0.25f, 0, 0,
        0, 0, -0.2f, 0, 0, 0, -1.2f, 1));
var request = new StormAovRequest(
    64, 64, 0,
    [StormAovKind.Color, StormAovKind.Depth, StormAovKind.PrimId,
        StormAovKind.InstanceId, StormAovKind.ElementId, StormAovKind.Neye, StormAovKind.Normal],
    camera, callerStateRevision: 17, callerSceneRevision: 23, includeIdentities: true);
using (OpenUsdStormRenderer renderer =
    OpenUsdStormRuntime.Create(pluginPath, stagePath))
{
    retained = renderer.RenderAovs(request);
    Require(retained.CallerStateRevision == 17 && retained.CallerSceneRevision == 23, "Caller claim binding.");
    StormAovOutput<float> depth = retained.GetOutput<float>(StormAovKind.Depth);
    Require(Math.Abs(depth.GetPixel(16, 31) - 0.2f) < 0.00001f, "Literal near depth.");
    Require(Math.Abs(depth.GetPixel(48, 31) - 0.6f) < 0.00001f, "Literal far depth.");
    Require(depth.GetPixel(0, 0) == 1, "Literal background depth.");
    Require(retained.GetIdentity(0, 0) is null, "Background is explicit.");
    Require(retained.GetIdentity(16, 31)?.PrimPath == "/World/Near", "Canonical near identity.");
    StormAovIdentity left = retained.GetIdentity(24, 15) ?? throw new InvalidOperationException();
    StormAovIdentity right = retained.GetIdentity(40, 15) ?? throw new InvalidOperationException();
    Require(left.PrimPath == "/World/Instances/Prototype" &&
        right.PrimPath == left.PrimPath &&
        left.RawInstanceId != right.RawInstanceId &&
        left.InstancerContext.Count == 1 && right.InstancerContext.Count == 1 &&
        left.InstancerContext[0].InstanceIndex == 0 &&
        right.InstancerContext[0].InstanceIndex == 1, "Distinct decoded point instances.");
    Require(retained.GetOutput(StormAovKind.ElementId).Status == StormAovStatus.Absent, "Element ID gap.");
    Require(retained.GetOutput(StormAovKind.Normal).Status == StormAovStatus.Unsupported, "Plain normal gap.");
    Require(retained.GetOutput<StormAovNeye>(StormAovKind.Neye).GetPixel(16, 31) ==
        new StormAovNeye(0, 0, 255, 255), "Raw quantized Neye.");
    Require(retained.Outputs is not ICollection && depth.Pixels is not ICollection &&
        left.InstancerContext is not ICollection, "Deep immutable collections.");
    StormAovSnapshot second = renderer.RenderAovs(request);
    Require(second.CaptureSequence > retained.CaptureSequence, "First native owner was released.");
    Require(retained.GetOutput<StormAovColor>(StormAovKind.Color).Pixels.Count == 4096, "Typed color payload.");
    StormAovSnapshot maximum = renderer.RenderAovs(new StormAovRequest(
        1024, 1024, 0, request.Outputs, camera, includeIdentities: true));
    Require(maximum.IdentityIndices.Count == StormAovLimits.MaximumPixels &&
        maximum.Identities.Count == 4 &&
        Math.Abs(maximum.GetOutput<float>(StormAovKind.Depth).GetPixel(256, 511) - 0.2f) < 0.00001f &&
        maximum.ManagedStorageUpperBound <= StormAovLimits.MaximumManagedBytes,
        "Exact public million-pixel admission and typed decoder.");
    RenderJobImage maximumImage = maximum.CreateJobImage(true, true, RenderOutputTransform.Reinhard, -6);
    Require(maximumImage.Rgba.Length == StormAovLimits.MaximumPixels * 4 &&
        maximumImage.HdrColor!.Rgba16Float.Length == StormAovLimits.MaximumPixels * 8 &&
        maximumImage.DeviceDepth!.Values.Length == StormAovLimits.MaximumPixels,
        "Exact public million-pixel job conversion.");
    Console.WriteLine("PUBLIC_AOV_JOB_EXACT_LIMIT=1048576");
    renderer.SetSelection(new SelectionState([new SelectionItem("/World/Near")]), new Vector4(1, 1, 0, 1));
    StormAovSnapshot selected = renderer.RenderAovs(request);
    Require(!selected.GetOutput<StormAovColor>(StormAovKind.Color).Pixels
        .SequenceEqual(retained.GetOutput<StormAovColor>(StormAovKind.Color).Pixels),
        "This selection fixture must detect highlights in native color.");
    bool selectedHdrRefused = false;
    try
    {
        _ = selected.CreateJobImage(includeHdrColor: true);
    }
    catch (NotSupportedException)
    {
        selectedHdrRefused = true;
    }
    Require(selectedHdrRefused && selected.CreateJobImage().HdrColor is null,
        "Selected color remains displayable but cannot be mislabeled pre-selection HDR.");
    renderer.SetSelection(SelectionState.Empty, Vector4.One);
    _ = renderer.RenderAovs(request).CreateJobImage(includeHdrColor: true);
    Console.WriteLine("PUBLIC_AOV_JOB_SELECTION_REFUSAL=passed");
    VerifyDiskJobs(renderer, camera, retained);
    Console.WriteLine(
        $"PUBLIC_AOV_CAPTURE=passed; pixels=4096; identities={retained.Identities.Count}; " +
        $"near={depth.GetPixel(16, 31)}; far={depth.GetPixel(48, 31)}; " +
        $"managedBound={retained.ManagedStorageUpperBound}");
    Console.WriteLine(
        $"PUBLIC_AOV_EXACT_LIMIT={maximum.IdentityIndices.Count}; identities={maximum.Identities.Count}; " +
        $"managedBound={maximum.ManagedStorageUpperBound}");
}
Require(retained.GetIdentity(16, 31)?.PrimPath == "/World/Near", "Snapshot survives renderer teardown.");
Require(retained.GetOutput<float>(StormAovKind.Depth).GetPixel(0, 0) == 1, "Detached pixel lifetime.");
RenderJobImage detachedImage = Task.Run(() => retained.CreateJobImage(true, true)).GetAwaiter().GetResult();
Require(detachedImage.DeviceDepth!.Values.Span[0] == 1 && detachedImage.HdrColor is not null,
    "Job conversion survives renderer disposal and needs no GL-owner thread.");
Console.WriteLine("PUBLIC_AOV_MANAGED_NATIVE_EXECUTION=passed");
StormChildAovProbe.Run(context.Window, pluginPath, stagePath, camera);
if (packageOnly)
{
    Require(!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported,
        "The package-only proof must execute native AOT, not a managed fallback.");
    Console.WriteLine("PUBLIC_AOV_PACKAGE_ONLY=passed");
    Console.WriteLine("PUBLIC_AOV_NATIVE_AOT=true");
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void VerifyDiskJobs(OpenUsdStormRenderer renderer, CameraState camera, StormAovSnapshot reference)
{
    string root = Directory.CreateTempSubdirectory("storm-aov-job-image-").FullName;
    try
    {
        StageRenderState first = StageRenderState.Create(new StageIdentity("storm-aov-fixture"))
            .WithViewport(new ViewportDimensions(64, 64)).WithCamera(camera)
            .WithRenderSettings(RenderSettings.PresentationDefault);
        StageRenderState second = first.WithTime(new StageTime(2)).AdvanceRevision();
        var source = new AovJobSource(renderer);
        RenderDiskJobResult raw = RenderDiskJob.Execute(
            new RenderDiskJobRequest(Path.Combine(root, "raw"), [first, second], true, true), source);
        Require(raw.Frames.Count == 2 && source.Calls == 2, "Ordered direct Storm disk captures.");
        RenderJobImage expected = reference.CreateJobImage(true, true, RenderOutputTransform.Reinhard, -6);
        foreach (RenderDiskFrameResult frame in raw.Frames)
        {
            byte[] hdr = File.ReadAllBytes(Path.Combine(raw.OutputDirectory, frame.HdrColorFileName!));
            Require(hdr.AsSpan().SequenceEqual(expected.HdrColor!.Rgba16Float.Span), "Exact native half bits in raw disk output.");
            Require(Convert.ToHexString(SHA256.HashData(hdr)).Equals(
                frame.HdrColorSha256, StringComparison.OrdinalIgnoreCase), "Raw output hash.");
            byte[] depth = File.ReadAllBytes(Path.Combine(raw.OutputDirectory, frame.DepthFileName!));
            float near = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(
                depth.AsSpan((31 * 64 + 16) * 4, 4)));
            Require(Math.Abs(near - 0.2f) < 0.00001f, "Unchanged normalized native depth in disk job.");
        }
        using (JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(raw.OutputDirectory, "manifest.json"))))
        {
            Require(manifest.RootElement.GetProperty("frames")[1].GetProperty("timeCode").GetDouble() == 2,
                "Disk job preserves captured time.");
            Require(manifest.RootElement.GetProperty("diagnostics").ToString().Contains("STORM_AOV_JOB_IMAGE", StringComparison.Ordinal),
                "Native output conversion provenance is retained.");
        }
        Console.WriteLine("PUBLIC_AOV_JOB_IMAGE=passed");
        RenderDiskJobResult exr = RenderDiskJob.Execute(
            new RenderDiskJobRequest(Path.Combine(root, "exr"), [first, second], true, true, RenderHdrColorFormat.Exr),
            source);
        Require(source.Calls == 4, "EXR jobs render their own current frames.");
        for (int index = 0; index < exr.Frames.Count; index++)
        {
            byte[] decoded = ExrScanlineOracle.Read(File.ReadAllBytes(Path.Combine(
                exr.OutputDirectory, exr.Frames[index].HdrColorFileName!)), 64, 64);
            Require(decoded.AsSpan().SequenceEqual(expected.HdrColor!.Rgba16Float.Span),
                "EXR independently decodes to the native Storm half-color plane.");
            Require(File.ReadAllBytes(Path.Combine(exr.OutputDirectory, exr.Frames[index].FileName))
                .AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(raw.OutputDirectory, raw.Frames[index].FileName))),
                "Switching raw HDR to EXR preserves the display PNG.");
        }
        Console.WriteLine("PUBLIC_AOV_JOB_EXR=passed");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

internal sealed class AovJobSource(OpenUsdStormRenderer renderer) : IRenderJobFrameSource
{
    internal int Calls { get; private set; }

    public RenderJobImage Render(StageRenderState state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StormAovSnapshot snapshot = renderer.RenderAovs(new StormAovRequest(
            state.Viewport.Width, state.Viewport.Height, 0, [StormAovKind.Color, StormAovKind.Depth],
            state.Camera, state.Time.TimeCode, state.Revision));
        if (snapshot.TimeCode != state.Time.TimeCode || snapshot.CallerStateRevision != state.Revision)
        {
            throw new InvalidOperationException("The Storm render did not return the job's current frame.");
        }
        Calls++;
        return snapshot.CreateJobImage(true, true, state.RenderSettings.OutputTransform,
            state.RenderSettings.Exposure, cancellationToken: cancellationToken);
    }
}
