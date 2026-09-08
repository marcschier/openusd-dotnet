// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace OpenUsd.Package.Tests;

public sealed partial class SilkHdrColorCapturePackageTests
{
    [Test]
    public async Task PackageOnlyConsumerCompilesEveryHdrOverloadWithNullableTrimAndAotAnalysis()
    {
        string packagePath = RequirePackage();
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        using Stream manifest = package.Entries.Single(entry =>
            entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
        string version = XDocument.Load(manifest).Descendants()
            .Single(element => element.Name.LocalName == "version").Value;
        string feed = Path.GetDirectoryName(Path.GetFullPath(packagePath))!;
        string workBase = Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT")
            ?? Path.Combine(AppContext.BaseDirectory, "hdr-package-work");
        string root = Path.Combine(workBase, "hdr-pkg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string project = Path.Combine(root, "HdrConsumer.csproj");
            new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup",
                    new XElement("TargetFrameworks", "net8.0;net9.0;net10.0"),
                    new XElement("Nullable", "enable"),
                    new XElement("ImplicitUsings", "enable"),
                    new XElement("LangVersion", "latest"),
                    new XElement("TreatWarningsAsErrors", "true"),
                    new XElement("IsAotCompatible", "true"),
                    new XElement("IsTrimmable", "true"),
                    new XElement("EnableTrimAnalyzer", "true"),
                    new XElement("EnableAotAnalyzer", "true")),
                new XElement("ItemGroup", new XElement("PackageReference",
                    new XAttribute("Include", "OpenUsd.Rendering.Silk"),
                    new XAttribute("Version", version))))).Save(project);
            new XDocument(new XElement("configuration",
                new XElement("packageSources", new XElement("clear"),
                    new XElement("add", new XAttribute("key", "hdr"),
                        new XAttribute("value", feed)),
                    new XElement("add", new XAttribute("key", "nuget.org"),
                        new XAttribute("value", "https://api.nuget.org/v3/index.json")))))
                .Save(Path.Combine(root, "nuget.config"));
            string host = Environment.ProcessPath is { } executable &&
                Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                ? executable
                : Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
            string[] sdkPaths = Path.IsPathFullyQualified(host) ? [Path.GetDirectoryName(host)!, "$host$"] : ["$host$"];
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), JsonSerializer.Serialize(new
            {
                sdk = new { version = "10.0.301", rollForward = "disable", paths = sdkPaths }
            }));
            await File.WriteAllTextAsync(Path.Combine(root, "CaptureConsumer.cs"), ConsumerSource);
            var start = new ProcessStartInfo(host)
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (string argument in new[]
            {
                "build", project, "-c", "Release", "-v:q", "-p:UseSharedCompilation=false"
            })
            {
                start.ArgumentList.Add(argument);
            }
            start.Environment.Remove("UseArtifactsOutput");
            start.Environment.Remove("ArtifactsPath");
            start.Environment["NUGET_PACKAGES"] = Path.Combine(root, "packages");
            start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
            using Process process = Process.Start(start)
                ?? throw new InvalidOperationException("The package consumer build did not start.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(5));
            }
            catch (TimeoutException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw;
            }
            string output = await stdout + await stderr;
            await Assert.That(process.ExitCode).IsEqualTo(0).Because(output);
            using JsonDocument assets = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(root, "obj", "project.assets.json")));
            JsonElement library = assets.RootElement.GetProperty("libraries")
                .GetProperty("OpenUsd.Rendering.Silk/" + version);
            await Assert.That(library.GetProperty("type").GetString()).IsEqualTo("package");
            foreach (string framework in new[] { "net8.0", "net9.0", "net10.0" })
            {
                await Assert.That(File.Exists(Path.Combine(root, "bin", "Release", framework, "HdrConsumer.dll")))
                    .IsTrue();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private const string ConsumerSource = """
        using OpenUsd.Rendering;
        using OpenUsd.Rendering.Silk;

        public static class CaptureConsumer
        {
            public static SilkFrameCaptureResult[] Capture(
                SilkFrameCapturer capturer, OpenUsdSilkSession session,
                ISilkRenderTargetRenderer renderer, ISilkGraphicsDevice device,
                SilkOpenColorIoProcessor processor, CancellationToken cancellationToken)
            {
                var options = new SilkHdrColorCaptureOptions(
                    includeDeviceDepth: true, maximumPixelCount: 1280, maximumReadbackBytes: 25600);
                return
                [
                    capturer.CaptureWithHdrColor(session, 40, 32, RenderSettings.Default),
                    capturer.CaptureWithHdrColor(session, 40, 32, RenderSettings.Default, processor),
                    capturer.CaptureWithHdrColor(
                        session, 40, 32, RenderSettings.Default, hdrColorOptions: options,
                        timeCode: 1, camera: default, cancellationToken: cancellationToken),
                    capturer.CaptureWithHdrColor(
                        session, 40, 32, RenderSettings.Default, processor, hdrColorOptions: options,
                        timeCode: 1, camera: default, cancellationToken: cancellationToken),
                    SilkFrameCapture.CaptureRetainedWithHdrColor(renderer, device, 40, 32, RenderSettings.Default),
                    SilkFrameCapture.CaptureRetainedWithHdrColor(
                        renderer, device, 40, 32, RenderSettings.Default, processor),
                    SilkFrameCapture.CaptureRetainedWithHdrColor(
                        renderer, device, 40, 32, RenderSettings.Default, hdrColorOptions: options,
                        pageRevision: 1, cancellationToken: cancellationToken),
                    SilkFrameCapture.CaptureRetainedWithHdrColor(
                        renderer, device, 40, 32, RenderSettings.Default, processor, hdrColorOptions: options,
                        pageRevision: 1, cancellationToken: cancellationToken),
                ];
            }

            public static ReadOnlyMemory<byte> Inspect(SilkFrameCaptureResult frame)
            {
                SilkHdrColorCaptureResult? hdr = frame.HdrColor;
                SilkDepthCaptureResult? depth = frame.Depth;
                if (hdr is null)
                {
                    return frame.Rgba;
                }
                if (hdr.Convention != SilkHdrColorConvention.RendererWorkingCompositedBeforeExposureAndDisplay ||
                    hdr.Width != frame.Width || hdr.Height != frame.Height ||
                    depth is not null && depth.Convention != SilkDepthConvention.NormalizedDeviceDepthZeroToOne)
                {
                    throw new InvalidDataException();
                }
                return hdr.Rgba16Float;
            }
        }
        """;
}
