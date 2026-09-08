// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections;
using System.Numerics;
using System.Runtime.InteropServices;
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
NativeLibrary.SetDllImportResolver(typeof(OpenUsdStormRuntime).Assembly, (name, _, _) =>
    name == "openusd_hydra"
        ? NativeLibrary.Load(libraryPath)
        : 0);
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
Console.WriteLine("PUBLIC_AOV_MANAGED_NATIVE_EXECUTION=passed");
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
