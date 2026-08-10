// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Runtime.Versioning;
using OpenUsd.Geom;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;

namespace OpenUsd.NativeCoverage.Tests;

public sealed class DataApiNativeCoverageTests
{
    [Test]
    public void AttributeArraysPrimvarsAndResolvedHandlesRoundTrip()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(AttributeArraysPrimvarsAndResolvedHandlesRoundTrip));
        using UsdStage stage = UsdStage.Create(Path.Combine(directory, "data-api.usda"));
        UsdPrim prim = stage.DefineMesh("/Mesh").Prim;

        prim.SetBoolArray("custom:flags", [true, false, true]);
        prim.SetBoolArray("custom:flags", [false, true], 12);
        prim.SetTokenArray("custom:states", ["cold", "hot"]);
        prim.SetStringArray("custom:labels", ["pump", "valve"], 12);
        new UsdGeomPrimvarsAPI(prim).SetDisplayColor(1, 0, 0);

        UsdAttribute displayColor = prim.GetAttribute("primvars:displayColor");
        RequireEqual(displayColor.TypeName, "color3f[]", "displayColor type");
        Require(displayColor.IsArray, "displayColor should report array type.");
        Require(displayColor.IsAuthored, "displayColor should report authored value.");
        UsdScalarValue colorValue = displayColor.GetValue();
        RequireEqual(colorValue.Kind, UsdScalarKind.Color3fArray, "displayColor scalar kind");
        RequireArrayEqual(colorValue.Color3fArrayValue, [new UsdVec3f(1, 0, 0)], "displayColor value");

        displayColor.Set(colorValue, 24);
        RequireArrayEqual(prim.GetColor3fArray("primvars:displayColor", 24), [new UsdVec3f(1, 0, 0)], "sampled color");
        Require(displayColor.TrySet(colorValue), "TrySet displayColor should succeed.");
        Require(displayColor.TryGetValue(out UsdScalarValue tryColor), "TryGet displayColor should succeed.");
        RequireEqual(tryColor.Kind, UsdScalarKind.Color3fArray, "TryGet color kind");

        UsdAttribute flags = prim.GetAttribute("custom:flags");
        RequireEqual(flags.GetValue().Kind, UsdScalarKind.BooleanArray, "bool array kind");
        RequireArrayEqual(prim.GetBoolArray("custom:flags"), [true, false, true], "bool array");
        RequireArrayEqual(prim.GetBoolArray("custom:flags", 12), [false, true], "sampled bool array");
        RequireArrayEqual(prim.GetTokenArray("custom:states"), ["cold", "hot"], "token array");
        RequireArrayEqual(prim.GetStringArray("custom:labels", 12), ["pump", "valve"], "sampled string array");
        Require(!displayColor.TrySet(flags.GetValue()), "TrySet should reject a mismatched value kind.");
        Require(!prim.TryGetValue("custom:missing", out _), "TryGetValue should reject a missing attribute.");
    }

    [Test]
    public void DisplayColorColor3fArrayChangesRenderedPixels()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        try
        {
            PrependHdSilkNativeSearchPath();
            string pluginPath = ResolvePluginPath();
            string directory = NativeCoverageRuntime.CreateTempDirectory(
                nameof(DisplayColorColor3fArrayChangesRenderedPixels));
            string stagePath = CreateDisplayColorStage(directory);
            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(pluginPath, stagePath);
            byte[] red = CaptureDisplayColor(session, 1);
            byte[] blue = CaptureDisplayColor(session, 2);

            (double redR, double redG, double redB) = AverageNonClearRgb(red);
            (double blueR, double blueG, double blueB) = AverageNonClearRgb(blue);
            Console.WriteLine(
                $"DISPLAY_COLOR_PIXEL red=({redR:F2},{redG:F2},{redB:F2}) " +
                $"blue=({blueR:F2},{blueG:F2},{blueB:F2})");
            Require(redR > redB + 20, "Red displayColor did not dominate rendered pixels.");
            Require(blueB > blueR + 20, "Blue displayColor did not dominate rendered pixels.");
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or DirectoryNotFoundException or OpenUsdSilkException)
        {
            Skip.Test($"displayColor render assertion skipped because hdSilk is unavailable: {exception.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static string CreateDisplayColorStage(string directory)
    {
        string stagePath = Path.Combine(directory, "display-color.usda");
        string root = FindRepositoryRoot() ??
            throw new DirectoryNotFoundException("Repository root was not found.");
        using UsdStage stage = UsdStage.Open(
            Path.Combine(root, "test-assets", "parity", "parity-points-asymmetric.usda"));
        UsdPrim points = stage.GetPrim("/World/PointCloud");
        points.SetColor3fArray("primvars:displayColor", [new UsdVec3f(1, 0, 0)], 1);
        points.SetColor3fArray("primvars:displayColor", [new UsdVec3f(0, 0, 1)], 2);
        stage.Export(stagePath);
        return stagePath;
    }

    [SupportedOSPlatform("windows")]
    private static byte[] CaptureDisplayColor(
        OpenUsdSilkSession session,
        double timeCode)
    {
        using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        var settings = new RenderSettings(
            1,
            enableLighting: false,
            enableShadows: false,
            new Vector4(0.02f, 0.02f, 0.02f, 1),
            backfaceCulling: false,
            useSceneMaterials: true,
            RenderComplexity.Low);
        SilkFrameCaptureResult capture = SilkFrameCapture.Capture(
            session,
            device,
            64,
            64,
            settings,
            timeCode,
            CameraState.Default);
        return capture.Rgba.ToArray();
    }

    private static (double Red, double Green, double Blue) AverageNonClearRgb(byte[] pixels)
    {
        long red = 0;
        long green = 0;
        long blue = 0;
        long count = 0;
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            byte r = pixels[offset];
            byte g = pixels[offset + 1];
            byte b = pixels[offset + 2];
            if (r < 8 && g < 8 && b < 8)
            {
                continue;
            }
            red += r;
            green += g;
            blue += b;
            ++count;
        }

        Require(count > 0, "No non-clear displayColor pixels were rendered.");
        return (red / (double)count, green / (double)count, blue / (double)count);
    }

    private static string ResolvePluginPath()
    {
        string? testConfigured = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
        if (!string.IsNullOrWhiteSpace(testConfigured) &&
            File.Exists(Path.Combine(testConfigured, "plugInfo.json")))
        {
            return testConfigured;
        }

        string packaged = Path.Combine(AppContext.BaseDirectory, "usd");
        if (File.Exists(Path.Combine(packaged, "plugInfo.json")))
        {
            return packaged;
        }

        if (TryPrepareLocalPluginRuntime(out string localRuntime))
        {
            return localRuntime;
        }

        string? configured = Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(Path.Combine(configured, "plugInfo.json")))
        {
            return configured;
        }

        throw new DirectoryNotFoundException(
            $"No OpenUSD plugin path was found under '{packaged}' or OPENUSD_PLUGIN_PATH.");
    }

    private static bool TryPrepareLocalPluginRuntime(out string pluginPath)
    {
        pluginPath = string.Empty;
        string? root = FindRepositoryRoot();
        if (root is null)
        {
            return false;
        }

        string build = Path.Combine(root, "native", "build", "shim", "win-x64");
        string stormPlugins = Path.Combine(
            build,
            "openusd_hydra",
            "tests",
            "storm-wgl-runtime",
            "plugin",
            "usd");
        string hdsilkPlugin = Path.Combine(build, "hdSilk", "resources", "plugInfo.json");
        string hdsilkLibrary = Path.Combine(build, "hdSilk", "openusd_hdsilk.dll");
        if (!File.Exists(Path.Combine(stormPlugins, "plugInfo.json")) ||
            !File.Exists(hdsilkPlugin) ||
            !File.Exists(hdsilkLibrary))
        {
            return false;
        }

        string runtime = Path.Combine(AppContext.BaseDirectory, "native-coverage-hdsilk", "runtime");
        string runtimePlugins = Path.Combine(runtime, "plugin", "usd");
        CopyDirectory(stormPlugins, runtimePlugins);
        string runtimeHdSilkResources = Path.Combine(runtimePlugins, "hdSilk", "resources");
        Directory.CreateDirectory(runtimeHdSilkResources);
        File.Copy(hdsilkPlugin, Path.Combine(runtimeHdSilkResources, "plugInfo.json"), overwrite: true);
        Directory.CreateDirectory(Path.Combine(runtime, "bin"));
        File.Copy(hdsilkLibrary, Path.Combine(runtime, "bin", "openusd_hdsilk.dll"), overwrite: true);
        pluginPath = runtimePlugins;
        return true;
    }

    private static void PrependHdSilkNativeSearchPath()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH")))
        {
            return;
        }

        string? root = FindRepositoryRoot();
        if (root is null)
        {
            return;
        }

        string[] directories =
        [
            Path.Combine(AppContext.BaseDirectory, "native-coverage-hdsilk", "runtime", "bin"),
            Path.Combine(root, "native", "install", "shim", "win-x64", "bin"),
            Path.Combine(root, "..", "openusd", "native", "install", "win-x64", "bin"),
            Path.Combine(root, "..", "openusd", "native", "install", "win-x64", "lib"),
            Path.Combine(root, "..", "openusd", "native", "install", "vulkan-sdk-1.4.321.0", "Bin")
        ];
        string prefix = string.Join(
            Path.PathSeparator,
            directories.Where(Directory.Exists).Select(Path.GetFullPath));
        Environment.SetEnvironmentVariable(
            "PATH",
            prefix + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? string.Empty));
    }

    private static string? FindRepositoryRoot()
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

        return null;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (string child in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(child, Path.Combine(destination, Path.GetFileName(child)));
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireEqual<T>(T actual, T expected, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private static void RequireArrayEqual<T>(T[] actual, T[] expected, string label)
    {
        if (!actual.SequenceEqual(expected))
        {
            throw new InvalidOperationException(
                $"{label}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
        }
    }
}
