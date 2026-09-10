// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Package.Tests;

public sealed partial class RuntimePackageTests
{
    [Test]
    public async Task StormPackagesExecuteBoundedAovsFromCleanNativeAotFeed()
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("OPENUSD_STORM_AOV_EXECUTION_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_STORM_AOV_EXECUTION_REQUIRED=1 on a Windows host with a working Storm GL context.");
            return;
        }
        string repositoryRoot = FindRepositoryRoot();
        if (!TryGetExecutionInputs(repositoryRoot, out NativeExecutionInputs inputs, out string reason))
        {
            throw new InvalidOperationException($"Required public Storm AOV execution is unavailable: {reason}");
        }
        string workRoot = CreateWorkRoot(repositoryRoot);
        try
        {
            string packageRoot = Directory.CreateDirectory(Path.Combine(workRoot, "packages")).FullName;
            string[] managedIds = ["OpenUsd.Interop", "OpenUsd", "OpenUsd.Rendering", "OpenUsd.Rendering.Storm"];
            string? version = null;
            foreach (string id in managedIds)
            {
                PackedPackage package = await PackManagedPackageAsync(repositoryRoot, id, packageRoot);
                version ??= package.Version;
                await Assert.That(package.Version).IsEqualTo(version);
            }
            foreach (string id in new[] { "OpenUsd.Runtime.Core.win-x64", "OpenUsd.Runtime.Imaging.win-x64" })
            {
                PackedPackage package = await PackAsync(repositoryRoot, id, inputs.InstallRoot,
                    inputs.ShimRoot, inputs.VulkanRuntimeLibrary, packageRoot);
                await Assert.That(package.Version).IsEqualTo(version);
            }
            string consumer = Directory.CreateDirectory(Path.Combine(workRoot, "storm-aov-consumer")).FullName;
            await File.WriteAllTextAsync(Path.Combine(consumer, "Directory.Build.props"), "<Project />");
            await File.WriteAllTextAsync(Path.Combine(consumer, "Directory.Packages.props"),
                "<Project><PropertyGroup><ManagePackageVersionsCentrally>false" +
                "</ManagePackageVersionsCentrally></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(consumer, "NuGet.config"), $"""
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="local" value="{System.Security.SecurityElement.Escape(packageRoot)}" />
                    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                  </packageSources>
                  <packageSourceMapping>
                    <packageSource key="local"><package pattern="OpenUsd*" /></packageSource>
                    <packageSource key="nuget.org">
                      <package pattern="Microsoft.*" /><package pattern="runtime.*" />
                    </packageSource>
                  </packageSourceMapping>
                </configuration>
                """);
            await File.WriteAllTextAsync(Path.Combine(consumer, "Consumer.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
                    <RuntimeIdentifier>win-x64</RuntimeIdentifier><PublishAot>true</PublishAot>
                    <AllowUnsafeBlocks>true</AllowUnsafeBlocks><ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                    <EnableTrimAnalyzer>true</EnableTrimAnalyzer><EnableAotAnalyzer>true</EnableAotAnalyzer>
                    <ILLinkTreatWarningsAsErrors>true</ILLinkTreatWarningsAsErrors>
                    <InvariantGlobalization>true</InvariantGlobalization>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="OpenUsd.Rendering.Storm" Version="{version}" />
                    <PackageReference Include="OpenUsd.Runtime.Imaging.win-x64" Version="{version}" />
                  </ItemGroup>
                  <ItemGroup>
                    <None Update="storm_aov_planes.usda" CopyToOutputDirectory="PreserveNewest"
                          CopyToPublishDirectory="PreserveNewest" />
                  </ItemGroup>
                </Project>
                """);
            string probe = Path.Combine(repositoryRoot, "tests", "OpenUsd.StormAov.NativeProbe");
            File.Copy(Path.Combine(probe, "Program.cs"), Path.Combine(consumer, "Program.cs"));
            File.Copy(Path.Combine(probe, "WindowsContext.cs"), Path.Combine(consumer, "WindowsContext.cs"));
            File.Copy(Path.Combine(probe, "StormChildAovProbe.cs"), Path.Combine(consumer, "StormChildAovProbe.cs"));
            File.Copy(Path.Combine(repositoryRoot, "tests", "OpenUsd.Exr.NativeProbe", "ExrScanlineOracle.cs"),
                Path.Combine(consumer, "ExrScanlineOracle.cs"));
            File.Copy(Path.Combine(repositoryRoot, "native", "openusd_hydra", "tests", "storm_aov_planes.usda"),
                Path.Combine(consumer, "storm_aov_planes.usda"));
            string publishRoot = Path.Combine(consumer, "publish");
            CommandResult publish = await RunDotnetAsync(consumer,
                ["publish", "Consumer.csproj", "-c", "Release", "--nologo", "--configfile", "NuGet.config",
                    "-o", publishRoot],
                Path.Combine(workRoot, "storm-aov-packages"));
            await Assert.That(publish.ExitCode).IsEqualTo(0).Because(publish.Output);
            AssertPackageOnlyGraph(Path.Combine(consumer, "obj", "project.assets.json"),
                [.. managedIds, "OpenUsd.Runtime.Core.win-x64", "OpenUsd.Runtime.Imaging.win-x64"]);
            await Assert.That(File.Exists(Path.Combine(publishRoot, "Consumer.dll"))).IsFalse();
            CommandResult execution = await RunExecutableAsync(
                GetExecutablePath(publishRoot, "Consumer"), publishRoot, []);
            await Assert.That(execution.ExitCode).IsEqualTo(0).Because(execution.Output);
            await Assert.That(execution.Output).Contains("PUBLIC_AOV_EXACT_LIMIT=1048576; identities=4;");
            await Assert.That(execution.Output).Contains("PUBLIC_AOV_MANAGED_NATIVE_EXECUTION=passed");
            await Assert.That(execution.Output).Contains("PUBLIC_AOV_PACKAGE_ONLY=passed");
            await Assert.That(execution.Output).Contains("PUBLIC_AOV_NATIVE_AOT=true");
            await Assert.That(execution.Output).Contains("PUBLIC_AOV_JOB_IMAGE=passed");
            await Assert.That(execution.Output).Contains("PUBLIC_AOV_JOB_EXR=passed");
            await Assert.That(execution.Output).Contains("PUBLIC_AOV_JOB_EXACT_LIMIT=1048576");
            await Assert.That(execution.Output).Contains("PUBLIC_AOV_JOB_SELECTION_REFUSAL=passed");
            await Assert.That(execution.Output).Contains("PUBLIC_CHILD_AOV_CAPTURE=passed");
            await Assert.That(execution.Output).Contains("PUBLIC_CHILD_AOV_SELECTION_REFUSAL=passed");
            await Assert.That(execution.Output).Contains("PUBLIC_CHILD_AOV_DETACHED_OWNER=passed");
            await Assert.That(execution.Output).Contains("PUBLIC_CHILD_AOV_EXACT_LIMIT=1048576");
            await Assert.That(execution.Output).Contains("PUBLIC_CHILD_AOV_REFUSAL_RECOVERY=passed");
            await AssertNoSourcePathLeakageAsync(execution.Output, repositoryRoot);
            Console.WriteLine(execution.Output);
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }
}
