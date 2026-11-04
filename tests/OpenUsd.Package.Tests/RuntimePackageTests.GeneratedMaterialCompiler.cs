// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace OpenUsd.Package.Tests;

public sealed partial class RuntimePackageTests
{
    [Test]
    public async Task SilkPackageGeneratedD3D12MaterialUsesPackagedSpirvCrossAndExternalDxc()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Generated D3D12 material compilation requires Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        string repositoryRoot = FindRepositoryRoot();
        string workRoot = CreateWorkRoot(repositoryRoot);
        try
        {
            string feed = Path.Combine(workRoot, "feed");
            string packages = Path.Combine(workRoot, "packages");
            Directory.CreateDirectory(feed);
            string[] packageIds =
            [
                "OpenUsd.Interop",
                "OpenUsd",
                "OpenUsd.Rendering",
                "OpenUsd.Rendering.Silk",
            ];
            var packed = new Dictionary<string, PackedPackage>(StringComparer.Ordinal);
            foreach (string id in packageIds)
            {
                packed.Add(id, await PackManagedPackageAsync(repositoryRoot, id, feed));
            }
            string version = packed["OpenUsd.Rendering.Silk"].Version;
            foreach (PackedPackage package in packed.Values)
            {
                await Assert.That(package.Version).IsEqualTo(version);
            }

            string crossVersion = ReadCentralPackageVersion(repositoryRoot, "Silk.NET.SPIRV.Cross.Native");
            string dxcVersion = ReadCentralPackageVersion(repositoryRoot, "Microsoft.Direct3D.DXC");
            foreach (string framework in new[] { "net8.0", "net9.0", "net10.0" })
            {
                string consumerRoot = Path.Combine(workRoot, framework);
                string publishRoot = Path.Combine(consumerRoot, "publish");
                Directory.CreateDirectory(consumerRoot);
                await File.WriteAllTextAsync(Path.Combine(consumerRoot, "Directory.Build.props"), "<Project />");
                await File.WriteAllTextAsync(
                    Path.Combine(consumerRoot, "Directory.Packages.props"),
                    "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>");
                File.Copy(
                    Path.Combine(repositoryRoot, "global.json"),
                    Path.Combine(consumerRoot, "global.json"));
                new XDocument(
                    new XElement("Project",
                        new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                        new XElement("PropertyGroup",
                            new XElement("OutputType", "Exe"),
                            new XElement("TargetFramework", framework),
                            new XElement("Nullable", "enable"),
                            new XElement("ImplicitUsings", "enable"),
                            new XElement("TreatWarningsAsErrors", "true")),
                        new XElement("ItemGroup",
                            new XElement("PackageReference",
                                new XAttribute("Include", "OpenUsd.Rendering.Silk"),
                                new XAttribute("Version", version)))))
                    .Save(Path.Combine(consumerRoot, "Consumer.csproj"));
                new XDocument(
                    new XElement("configuration",
                        new XElement("packageSources",
                            new XElement("clear"),
                            new XElement("add", new XAttribute("key", "candidate"), new XAttribute("value", feed)),
                            new XElement("add", new XAttribute("key", "nuget.org"),
                                new XAttribute("value", "https://api.nuget.org/v3/index.json"))),
                        new XElement("packageSourceMapping",
                            new XElement("packageSource", new XAttribute("key", "candidate"),
                                new XElement("package", new XAttribute("pattern", "OpenUsd*"))),
                            new XElement("packageSource", new XAttribute("key", "nuget.org"),
                                new XElement("package", new XAttribute("pattern", "*"))))))
                    .Save(Path.Combine(consumerRoot, "NuGet.config"));
                await File.WriteAllTextAsync(
                    Path.Combine(consumerRoot, "Program.cs"),
                    GeneratedMaterialCompilerConsumerSource);

                CommandResult publish = await RunDotnetAsync(
                    consumerRoot,
                    [
                        "publish", "Consumer.csproj", "-c", "Release", "-r", "win-x64",
                        "--self-contained", "false", "--nologo", "-m:1",
                        "-p:UseSharedCompilation=false", "-p:UseArtifactsOutput=false",
                        "--configfile", "NuGet.config", "-o", publishRoot,
                    ],
                    packages);
                await Assert.That(publish.ExitCode).IsEqualTo(0).Because(publish.Output);

                using JsonDocument assets = JsonDocument.Parse(await File.ReadAllTextAsync(
                    Path.Combine(consumerRoot, "obj", "project.assets.json")));
                JsonElement libraries = assets.RootElement.GetProperty("libraries");
                foreach ((string id, PackedPackage package) in packed)
                {
                    JsonElement library = libraries.GetProperty($"{id}/{version}");
                    await Assert.That(library.GetProperty("type").GetString()).IsEqualTo("package");
                    string restoredDirectory = Path.Combine(
                        packages,
                        library.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
                    string restoredPackage = Directory.GetFiles(restoredDirectory, "*.nupkg").Single();
                    byte[] restoredBytes = await File.ReadAllBytesAsync(restoredPackage);
                    byte[] candidateBytes = await File.ReadAllBytesAsync(package.Path);
                    await Assert.That(SHA256.HashData(restoredBytes)
                        .AsSpan().SequenceEqual(SHA256.HashData(candidateBytes)))
                        .IsTrue().Because($"{framework} must restore the candidate {id} package.");
                }

                string restoredCross = Path.Combine(
                    packages, "silk.net.spirv.cross.native", crossVersion.ToLowerInvariant(),
                    "runtimes", "win-x64", "native", "spirv-cross.dll");
                string publishedCross = Path.Combine(publishRoot, "spirv-cross.dll");
                await Assert.That(File.Exists(restoredCross)).IsTrue().Because(framework);
                await Assert.That(File.Exists(publishedCross)).IsTrue().Because(framework);
                byte[] restoredCrossBytes = await File.ReadAllBytesAsync(restoredCross);
                byte[] publishedCrossBytes = await File.ReadAllBytesAsync(publishedCross);
                await Assert.That(SHA256.HashData(restoredCrossBytes)
                    .AsSpan().SequenceEqual(SHA256.HashData(publishedCrossBytes)))
                    .IsTrue().Because($"{framework} must publish the restored SPIRV-Cross asset.");
                foreach (string name in new[] { "dxc.exe", "dxcompiler.dll", "dxil.dll" })
                {
                    await Assert.That(File.Exists(Path.Combine(publishRoot, name)))
                        .IsFalse().Because("DXC must be supplied outside the Silk package.");
                }

                string dxcSource = Path.Combine(
                    packages, "microsoft.direct3d.dxc", dxcVersion.ToLowerInvariant(),
                    "build", "native", "bin", "x64");
                string externalDxc = Path.Combine(consumerRoot, "external-dxc");
                Directory.CreateDirectory(externalDxc);
                foreach (string name in new[] { "dxc.exe", "dxcompiler.dll", "dxil.dll" })
                {
                    File.Copy(Path.Combine(dxcSource, name), Path.Combine(externalDxc, name));
                }

                string executable = Path.Combine(publishRoot, "Consumer.exe");
                string cache = Path.Combine(consumerRoot, "shader-cache");
                CommandResult missing = await RunGeneratedMaterialConsumerAsync(
                    executable, publishRoot, cache, "missing", string.Empty);
                await Assert.That(missing.ExitCode).IsEqualTo(0).Because(missing.Output);
                await Assert.That(missing.Output).Contains("MISSING_DXC_REPORTED");

                CommandResult compiled = await RunGeneratedMaterialConsumerAsync(
                    executable, publishRoot, cache, "external", Path.Combine(externalDxc, "dxc.exe"));
                await Assert.That(compiled.ExitCode).IsEqualTo(0).Because(compiled.Output);
                await Assert.That(compiled.Output).Contains("PACKAGED_SPIRV_CROSS_AND_EXTERNAL_DXC_OK");
            }
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    private static string ReadCentralPackageVersion(string repositoryRoot, string packageId) =>
        XDocument.Load(Path.Combine(repositoryRoot, "Directory.Packages.props"))
            .Descendants("PackageVersion")
            .Single(element => element.Attribute("Include")?.Value == packageId)
            .Attribute("Version")?.Value ??
            throw new InvalidOperationException($"The {packageId} dependency has no central version.");

    private static string[] SplitDependencyAssets(string? value) =>
        value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    private static async Task<CommandResult> RunGeneratedMaterialConsumerAsync(
        string executable,
        string workingDirectory,
        string cache,
        string mode,
        string dxc)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        process.StartInfo.ArgumentList.Add(mode);
        process.StartInfo.Environment["PATH"] = workingDirectory;
        process.StartInfo.Environment["OPENUSD_DXC_PATH"] = dxc;
        process.StartInfo.Environment["OPENUSD_SILK_SHADER_CACHE_DIR"] = cache;
        process.StartInfo.Environment.Remove("DOTNET_ADDITIONAL_DEPS");
        process.StartInfo.Environment.Remove("DOTNET_SHARED_STORE");
        process.StartInfo.Environment.Remove("DOTNET_STARTUP_HOOKS");
        if (!process.Start())
        {
            throw new InvalidOperationException("The generated-material package consumer did not start.");
        }
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
        }
        catch (TimeoutException exception)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync();
            throw new TimeoutException(
                $"The generated-material consumer timed out in '{mode}' mode.{Environment.NewLine}" +
                $"{await stdout}{Environment.NewLine}{await stderr}",
                exception);
        }
        return new CommandResult(process.ExitCode, $"{await stdout}{Environment.NewLine}{await stderr}");
    }

    private const string GeneratedMaterialCompilerConsumerSource = """
        using System.Diagnostics;
        using System.Text;
        using OpenUsd.Rendering.Silk;

        if (!OperatingSystem.IsWindows() || args.Length != 1 ||
            args[0] is not ("missing" or "external"))
        {
            throw new InvalidOperationException("The Windows consumer needs a compiler mode.");
        }

        var generator = new SilkProjectedMaterialShaderGenerator();
        SilkMaterialShaderKey key = SilkMaterialShaderKey.Create(
            Encoding.UTF8.GetBytes("packaged-material"),
            SilkShaderBinaryFormat.Dxil,
            "package-compiler-v1");
        generator.RegisterGenerated(
            key,
            SilkCheckedShaderAssets.LoadSelectionMaskFragment(SilkShaderBinaryFormat.SpirV).Code);
        SilkMaterialShaderProgram? program = null;
        try
        {
            program = await generator.CompileAsync(key, CancellationToken.None);
        }
        catch (FileNotFoundException exception) when (
            args[0] == "missing" && exception.Message.Contains("dxc.exe", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("MISSING_DXC_REPORTED");
        }

        using Process current = Process.GetCurrentProcess();
        string[] modules = current.Modules.Cast<ProcessModule>()
            .Where(module => string.Equals(module.ModuleName, "spirv-cross.dll", StringComparison.OrdinalIgnoreCase))
            .Select(module => Path.GetFullPath(module.FileName!))
            .ToArray();
        string expectedCross = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "spirv-cross.dll"));
        if (modules.Length != 1 || !string.Equals(modules[0], expectedCross, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The packaged SPIRV-Cross library was not loaded.");
        }

        if (args[0] == "missing")
        {
            if (program is not null)
            {
                throw new InvalidOperationException("Compilation succeeded without an external DXC.");
            }
            return;
        }

        SilkMaterialShaderProgram compiled = program ??
            throw new InvalidOperationException("Compilation did not return a shader.");
        byte[] dxil = compiled.FragmentShader.Code.ToArray();
        if (compiled.FragmentShader.Format != SilkShaderBinaryFormat.Dxil ||
            dxil.Length < 4 || Encoding.ASCII.GetString(dxil, 0, 4) != "DXBC")
        {
            throw new InvalidDataException("The generated shader is not a DXIL container.");
        }
        string scratch = Path.Combine(
            Environment.GetEnvironmentVariable("OPENUSD_SILK_SHADER_CACHE_DIR")!, "dxc");
        if (!Directory.Exists(scratch) || Directory.GetFiles(scratch).Length != 0)
        {
            throw new InvalidOperationException("DXC scratch files were not cleaned.");
        }
        Console.WriteLine("PACKAGED_SPIRV_CROSS_AND_EXTERNAL_DXC_OK");
        """;
}
