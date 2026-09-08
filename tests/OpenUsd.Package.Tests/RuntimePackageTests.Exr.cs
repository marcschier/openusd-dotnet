// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;

namespace OpenUsd.Package.Tests;

public sealed partial class RuntimePackageTests
{
    private const string ExrExecutionRequiredVariable = "OPENUSD_EXR_EXECUTION_REQUIRED";

    [Test]
    public async Task CorePackagesExecuteBoundedExrFromCleanNativeAotFeed()
    {
        if (Environment.GetEnvironmentVariable(ExrExecutionRequiredVariable) != "1")
        {
            Skip.Test("Set OPENUSD_EXR_EXECUTION_REQUIRED=1 to require the Windows x64 Core EXR execution proof.");
            return;
        }
        if (!OperatingSystem.IsWindows() ||
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture !=
                System.Runtime.InteropServices.Architecture.X64)
        {
            throw new InvalidOperationException("Required Core EXR execution needs the Windows x64 handle profile.");
        }
        if (RequiredDataAbiVersion < 24 || (RequiredDataCapabilities & (1UL << 32)) == 0)
        {
            throw new InvalidOperationException(
                "Required Core EXR execution needs the ABI24-or-later guarded EXR contract; no execution was attempted.");
        }
        string repositoryRoot = FindRepositoryRoot();
        if (!TryGetExecutionInputs(repositoryRoot, out NativeExecutionInputs inputs, out string reason))
        {
            throw new InvalidOperationException($"Required Core EXR execution is unavailable: {reason}");
        }

        string expectedDataSha256 = GetFileSha256(Path.Combine(
            inputs.ShimRoot, "bin", inputs.Platform.DotnetLibrary));
        string expectedUsdSha256 = GetFileSha256(Path.Combine(
            inputs.InstallRoot, "lib", inputs.Platform.OpenUsdLibrary));
        string workRoot = CreateWorkRoot(repositoryRoot);
        try
        {
            string packageRoot = Directory.CreateDirectory(Path.Combine(workRoot, "packages")).FullName;
            string[] managedIds = ["OpenUsd.Interop", "OpenUsd", "OpenUsd.Rendering"];
            var produced = new List<PackedPackage>();
            string? version = null;
            foreach (string id in managedIds)
            {
                PackedPackage package = await PackManagedPackageAsync(repositoryRoot, id, packageRoot);
                version ??= package.Version;
                await Assert.That(package.Version).IsEqualTo(version);
                produced.Add(package);
            }
            PackedPackage core = await PackAsync(
                repositoryRoot, "OpenUsd.Runtime.Core.win-x64", inputs.InstallRoot,
                inputs.ShimRoot, inputs.VulkanRuntimeLibrary, packageRoot);
            await Assert.That(core.Version).IsEqualTo(version);
            produced.Add(core);
            await AssertPackageEntryMatchesFileAsync(
                core.Path, "runtimes/win-x64/native/openusd_dotnet.dll",
                Path.Combine(inputs.ShimRoot, "bin", inputs.Platform.DotnetLibrary));
            await AssertPackageEntryMatchesFileAsync(
                core.Path, "runtimes/win-x64/native/usd_ms.dll",
                Path.Combine(inputs.InstallRoot, "lib", inputs.Platform.OpenUsdLibrary));
            await AssertPackageDoesNotContainAsync(core.Path, "ntdll.dll");

            string consumer = Directory.CreateDirectory(Path.Combine(workRoot, "exr-consumer")).FullName;
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
                    <DisableRuntimeMarshalling>true</DisableRuntimeMarshalling>
                    <IsAotCompatible>true</IsAotCompatible>
                    <EnableTrimAnalyzer>true</EnableTrimAnalyzer><EnableAotAnalyzer>true</EnableAotAnalyzer>
                    <ILLinkTreatWarningsAsErrors>true</ILLinkTreatWarningsAsErrors>
                    <InvariantGlobalization>true</InvariantGlobalization>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="OpenUsd.Rendering" Version="{version}" />
                    <PackageReference Include="OpenUsd.Runtime.Core.win-x64" Version="{version}" />
                  </ItemGroup>
                </Project>
                """);
            string probe = Path.Combine(repositoryRoot, "tests", "OpenUsd.Exr.NativeProbe");
            foreach (string name in new[] { "Program.cs", "ExrScanlineOracle.cs" })
            {
                File.Copy(Path.Combine(probe, name), Path.Combine(consumer, name));
            }
            string publishRoot = Path.Combine(consumer, "publish");
            CommandResult publish = await RunDotnetAsync(consumer,
                ["publish", "Consumer.csproj", "-c", "Release", "--nologo",
                    "--configfile", "NuGet.config", "-o", publishRoot],
                Path.Combine(workRoot, "exr-packages"));
            await Assert.That(publish.ExitCode).IsEqualTo(0).Because(publish.Output);
            AssertPackageOnlyGraph(Path.Combine(consumer, "obj", "project.assets.json"),
                [.. managedIds, "OpenUsd.Runtime.Core.win-x64"]);
            await Assert.That(File.Exists(Path.Combine(publishRoot, "Consumer.dll"))).IsFalse();
            await Assert.That(File.Exists(Path.Combine(publishRoot, "openusd_hydra.dll"))).IsFalse();
            await Assert.That(File.Exists(Path.Combine(publishRoot, "openusd_hdsilk.dll"))).IsFalse();

            string executable = GetExecutablePath(publishRoot, "Consumer");
            CommandResult execution = await RunExecutableAsync(
                executable, publishRoot, [expectedDataSha256, expectedUsdSha256]);
            await Assert.That(execution.ExitCode).IsEqualTo(0).Because(execution.Output);
            await Assert.That(execution.Output).Contains("PUBLIC_EXR_NATIVE_AOT=true");
            await Assert.That(execution.Output).Contains("PUBLIC_EXR_INDEPENDENT_HALF_BITS=passed");
            await Assert.That(execution.Output).Contains("PUBLIC_EXR_QUOTA_CANCEL_OWNERSHIP=passed");
            await Assert.That(execution.Output).Contains("PUBLIC_EXR_DISK_JOB=passed");
            await Assert.That(execution.Output).Contains("PUBLIC_EXR_PACKAGE_ONLY=passed");
            await AssertNoSourcePathLeakageAsync(execution.Output, repositoryRoot);
            Console.WriteLine(execution.Output);

            string receiptRoot = Directory.CreateDirectory(Path.Combine(
                Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT") ??
                    Path.Combine(repositoryRoot, "artifacts"),
                "exr-execution-receipts")).FullName;
            string receipt = Path.Combine(receiptRoot, $"core-exr-{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(receipt, JsonSerializer.Serialize(new
            {
                Test = nameof(CorePackagesExecuteBoundedExrFromCleanNativeAotFeed),
                RequiredSwitch = ExrExecutionRequiredVariable,
                PackageVersion = version,
                DataAbi = RequiredDataAbiVersion,
                DataCapabilities = RequiredDataCapabilities,
                DataSha256 = expectedDataSha256,
                SdkSha256 = expectedUsdSha256,
                NativeExecutableSha256 = GetFileSha256(executable),
                ProbeSourceSha256 = GetFileSha256(Path.Combine(probe, "Program.cs")),
                OracleSourceSha256 = GetFileSha256(Path.Combine(probe, "ExrScanlineOracle.cs")),
                Packages = produced.Select(package => new
                {
                    FileName = Path.GetFileName(package.Path),
                    Sha256 = GetFileSha256(package.Path)
                }).ToArray(),
                Execution = execution.Output,
                NativeCodecHeapQuotaClaimed = false
            }, IndentedJsonOptions));
            Console.WriteLine($"PUBLIC_EXR_EXECUTION_RECEIPT={receipt}");
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }
}
