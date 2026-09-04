// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace OpenUsd.Package.Tests;

/// <summary>
/// Proves the optional Omniverse bridge ships as ordinary NuGet packages: a consumer restores them
/// from a clean feed with no project reference, runs the protocol against a coordinator, and
/// publishes NativeAOT without a trim or AOT warning.
/// </summary>
[NotInParallel]
public sealed class BridgePackageTests
{
    private const string RequiredExecutionEnvironmentVariable =
        "OPENUSD_PACKAGE_EXECUTION_REQUIRED";

    private static readonly string[] BridgePackageIds =
    [
        "OpenUsd.Interop",
        "OpenUsd",
        "OpenUsd.LiveAuthoring",
        "OpenUsd.Bridge.Protocol",
        "OpenUsd.Bridge.Grpc"
    ];

    [Test]
    public async Task TheAuthoringPackageCarriesNoNetworkingDependency()
    {
        string repositoryRoot = FindRepositoryRoot();
        XDocument authoring = XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "OpenUsd.LiveAuthoring",
            "OpenUsd.LiveAuthoring.csproj"));

        string[] packageReferences = [.. authoring
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)];

        foreach (string reference in packageReferences)
        {
            await Assert.That(reference).DoesNotContain("Grpc");
            await Assert.That(reference).DoesNotContain("Protobuf");
        }

        string[] projectReferences = [.. authoring
            .Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(
                (element.Attribute("Include")?.Value ?? string.Empty)
                    .Replace('\\', '/')) ?? string.Empty)];
        await Assert.That(projectReferences).IsEquivalentTo(["OpenUsd"]);
    }

    [Test]
    public async Task TheBridgePackagesCarryTheProtoContractAndNoNvidiaComponent()
    {
        string repositoryRoot = FindRepositoryRoot();
        string workRoot = CreateWorkRoot(repositoryRoot);
        try
        {
            string packageRoot = Path.Combine(workRoot, "packages");
            Directory.CreateDirectory(packageRoot);
            PackedPackage protocol = await PackAsync(
                repositoryRoot,
                "OpenUsd.Bridge.Protocol",
                packageRoot);
            PackedPackage grpc = await PackAsync(repositoryRoot, "OpenUsd.Bridge.Grpc", packageRoot);

            using ZipArchive protocolArchive = ZipFile.OpenRead(protocol.Path);
            string[] entries = [.. protocolArchive.Entries.Select(entry => entry.FullName)];

            await Assert.That(entries.Any(entry =>
                entry.EndsWith("openusd/bridge/v1/wire.proto", StringComparison.Ordinal))).IsTrue();
            await Assert.That(entries.Any(entry =>
                entry.EndsWith("openusd/bridge/v1/service.proto", StringComparison.Ordinal))).IsTrue();
            await Assert.That(entries.Any(entry =>
                entry.StartsWith("contentFiles/", StringComparison.Ordinal))).IsFalse()
                .Because(
                    "raw proto contracts must not be imported into consuming projects as source items");
            foreach (string framework in new[] { "net8.0", "net9.0", "net10.0" })
            {
                await Assert.That(entries).Contains(
                    $"lib/{framework}/OpenUsd.Bridge.Protocol.dll");
            }

            foreach (string packagePath in new[] { protocol.Path, grpc.Path })
            {
                using ZipArchive archive = ZipFile.OpenRead(packagePath);
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    await Assert.That(entry.FullName.Contains("omni", StringComparison.OrdinalIgnoreCase))
                        .IsFalse()
                        .Because($"{packagePath} carries '{entry.FullName}'.");
                    await Assert.That(entry.FullName.Contains("nvidia", StringComparison.OrdinalIgnoreCase))
                        .IsFalse()
                        .Because($"{packagePath} carries '{entry.FullName}'.");
                }
            }

            await AssertPackageDependsOnAsync(grpc.Path, "OpenUsd.Bridge.Protocol");
            await AssertPackageDependsOnAsync(grpc.Path, "Grpc.Net.Client");
            await AssertPackageDependsOnAsync(protocol.Path, "Google.Protobuf");
            await AssertPackageDoesNotDependOnAsync(protocol.Path, "Grpc.Net.Client");
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    [Test]
    public async Task PackageOnlyBridgeConsumerAppliesASnapshotAndOrderedDeltas()
    {
        string repositoryRoot = FindRepositoryRoot();
        string workRoot = CreateWorkRoot(repositoryRoot);
        try
        {
            string packageRoot = Path.Combine(workRoot, "packages");
            Directory.CreateDirectory(packageRoot);
            Dictionary<string, PackedPackage> packages = await PackBridgeGraphAsync(
                repositoryRoot,
                packageRoot);

            string consumerRoot = Path.Combine(workRoot, "bridge-consumer");
            await CreateConsumerAsync(consumerRoot, packageRoot, packages, publishAot: false);

            CommandResult run = await RunDotnetAsync(
                consumerRoot,
                ["run", "--project", Path.Combine(consumerRoot, "Consumer.csproj"), "-c", "Release"]);

            Console.WriteLine(run.Output);
            await Assert.That(run.ExitCode).IsEqualTo(0).Because(run.Output);
            await AssertConsumerOutputAsync(run.Output, repositoryRoot);
            AssertPackageOnlyGraph(
                Path.Combine(consumerRoot, "obj", "project.assets.json"),
                BridgePackageIds);
            string consumerProject = await File.ReadAllTextAsync(
                Path.Combine(consumerRoot, "Consumer.csproj"));
            await Assert.That(consumerProject).DoesNotContain("ProjectReference");
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    [Test]
    public async Task PackageOnlyBridgeConsumerPublishesAndExecutesNativeAot()
    {
        string repositoryRoot = FindRepositoryRoot();
        string workRoot = CreateWorkRoot(repositoryRoot);
        try
        {
            string packageRoot = Path.Combine(workRoot, "packages");
            Directory.CreateDirectory(packageRoot);
            Dictionary<string, PackedPackage> packages = await PackBridgeGraphAsync(
                repositoryRoot,
                packageRoot);

            string consumerRoot = Path.Combine(workRoot, "bridge-aot-consumer");
            await CreateConsumerAsync(consumerRoot, packageRoot, packages, publishAot: true);

            string publishRoot = Path.Combine(consumerRoot, "publish");
            CommandResult publish = await RunDotnetAsync(
                consumerRoot,
                [
                    "publish",
                    Path.Combine(consumerRoot, "Consumer.csproj"),
                    "-c",
                    "Release",
                    "-o",
                    publishRoot
                ],
                CreateNativeToolchainEnvironment());

            if (publish.ExitCode != 0 && IsMissingAotToolchain(publish.Output))
            {
                HandleMissingExecutionPrerequisites(
                    nameof(PackageOnlyBridgeConsumerPublishesAndExecutesNativeAot),
                    "the host has no native AOT toolchain (a platform linker is required)");
                return;
            }

            Console.WriteLine(publish.Output);
            await Assert.That(publish.ExitCode).IsEqualTo(0).Because(publish.Output);

            // Trimming and AOT analysis run as errors in this consumer, so a clean publish is the
            // gate. The warning scan below keeps a downgraded analyzer from passing silently.
            await Assert.That(publish.Output).DoesNotContain("IL2026");
            await Assert.That(publish.Output).DoesNotContain("IL2104");
            await Assert.That(publish.Output).DoesNotContain("IL3050");
            await Assert.That(publish.Output).DoesNotContain("IL3053");

            CommandResult run = await RunExecutableAsync(
                Path.Combine(
                    publishRoot,
                    OperatingSystem.IsWindows() ? "Consumer.exe" : "Consumer"),
                publishRoot);

            Console.WriteLine(run.Output);
            await Assert.That(run.ExitCode).IsEqualTo(0).Because(run.Output);
            await AssertConsumerOutputAsync(run.Output, repositoryRoot);
            await Assert.That(run.Output).Contains("NATIVE_AOT=True");
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    private static async Task AssertConsumerOutputAsync(string output, string repositoryRoot)
    {
        await Assert.That(output).Contains("BRIDGE_PROTOCOL=openusd.bridge.v1");
        await Assert.That(output).Contains("BRIDGE_VERSION=1.0");
        await Assert.That(output).Contains("DELTA_ROUNDTRIP=True");
        await Assert.That(output).Contains("SNAPSHOT_APPLIED=True");
        await Assert.That(output).Contains("DELTA_APPLIED=True");
        await Assert.That(output).Contains("GAP_REQUIRES_RESYNC=True");
        await Assert.That(output).Contains("MALFORMED_REJECTED=True");
        await Assert.That(output).Contains("LOOPBACK_ONLY_ENFORCED=True");
        await Assert.That(output).Contains("CREDENTIAL_REDACTED=True");
        await Assert.That(output).Contains("ORIGIN_ADOPTED=True");
        await Assert.That(output).Contains("GENERATED_ORIGIN_UNIQUE=True");
        await Assert.That(output).Contains("ILL_FORMED_ORIGIN_REJECTED=True");
        await Assert.That(output).Contains("INDETERMINATE_OUTCOME=Indeterminate");
        await Assert.That(output).Contains("DESCRIPTOR_BYTES=");
        await Assert.That(output).DoesNotContain(repositoryRoot);
    }

    private static async Task<Dictionary<string, PackedPackage>> PackBridgeGraphAsync(
        string repositoryRoot,
        string packageRoot)
    {
        var packages = new Dictionary<string, PackedPackage>(StringComparer.Ordinal);
        foreach (string packageId in BridgePackageIds)
        {
            packages[packageId] = await PackAsync(repositoryRoot, packageId, packageRoot);
        }

        return packages;
    }

    private static async Task CreateConsumerAsync(
        string consumerRoot,
        string packageRoot,
        Dictionary<string, PackedPackage> packages,
        bool publishAot)
    {
        Directory.CreateDirectory(consumerRoot);
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "Directory.Build.props"),
            "<Project />");
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "Directory.Packages.props"),
            """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "NuGet.config"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="isolated-openusd-feed" value="{packageRoot}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="isolated-openusd-feed">
                  <package pattern="OpenUsd*" />
                </packageSource>
                <packageSource key="nuget.org">
                  <package pattern="Microsoft.*" />
                  <package pattern="runtime.*" />
                  <package pattern="Google.*" />
                  <package pattern="Grpc.*" />
                  <package pattern="System.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "Consumer.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <PublishAot>{publishAot.ToString().ToLowerInvariant()}</PublishAot>
                <IsAotCompatible>true</IsAotCompatible>
                <EnableAotAnalyzer>true</EnableAotAnalyzer>
                <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
                <EnableSingleFileAnalyzer>true</EnableSingleFileAnalyzer>
                <InvariantGlobalization>true</InvariantGlobalization>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="OpenUsd.Interop"
                                  Version="{packages["OpenUsd.Interop"].Version}" />
                <PackageReference Include="OpenUsd" Version="{packages["OpenUsd"].Version}" />
                <PackageReference Include="OpenUsd.LiveAuthoring"
                                  Version="{packages["OpenUsd.LiveAuthoring"].Version}" />
                <PackageReference Include="OpenUsd.Bridge.Protocol"
                                  Version="{packages["OpenUsd.Bridge.Protocol"].Version}" />
                <PackageReference Include="OpenUsd.Bridge.Grpc"
                                  Version="{packages["OpenUsd.Bridge.Grpc"].Version}" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(consumerRoot, "Program.cs"), ConsumerProgram);
    }

    /// <summary>
    /// A package-only consumer that exercises the whole bridge contract without a peer: the wire
    /// codec, the coordinator handoff, gap-driven resync, malformed-input rejection, and the
    /// client's loopback and credential rules.
    /// </summary>
    private const string ConsumerProgram = """
        using System.Runtime.CompilerServices;
        using OpenUsd.Bridge.Grpc;
        using OpenUsd.Bridge.Protocol;
        using OpenUsd.LiveAuthoring;

        const string bridgeRoot = "/Bridge";
        const string localOrigin = "openusd-local";
        var epoch = new LiveAuthoringRemoteEpoch("kit-bridge", "session-a", 1);

        var delta = new LiveAuthoringDelta(
            epoch,
            1,
            [
                new SetAttributeUpdate(
                    $"{bridgeRoot}/Cube",
                    "custom:pressure",
                    LiveAttributeValue.FromDouble(1.5))
            ],
            originId: "kit-bridge");
        byte[] encoded = BridgeWireCodec.EncodeDelta(delta);
        bool deltaRoundTrip = BridgeWireCodec.TryDecodeDelta(
            encoded,
            out LiveAuthoringDelta? decodedDelta,
            out BridgeWireError decodeError) && decodedDelta!.Sequence == 1;

        bool malformedRejected = !BridgeWireCodec.TryDecodeDelta(
            new byte[] { 0xFF, 0xFF, 0xFF, 0xFF },
            out _,
            out BridgeWireError malformedError) && malformedError.IsError;

        var executor = new OverlayExecutor();
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 8);
        await using var coordinator = new LiveAuthoringSessionCoordinator(
            sink,
            new LiveAuthoringSessionOptions
            {
                BridgeRootPath = bridgeRoot,
                LocalOriginId = localOrigin
            });

        await coordinator.ConnectAsync(epoch);
        bool snapshotDecoded = BridgeWireCodec.TryDecodeSnapshot(
            BridgeWireCodec.EncodeSnapshot(new LiveAuthoringSnapshot(
                epoch,
                0,
                bridgeRoot,
                [new DefinePrimUpdate($"{bridgeRoot}/Cube", "Xform")])),
            out LiveAuthoringSnapshot? snapshot,
            out _);
        LiveAuthoringSessionResult applied = await coordinator.ApplySnapshotAsync(snapshot!);
        LiveAuthoringSessionResult deltaApplied = await coordinator.ApplyDeltaAsync(decodedDelta!);
        LiveAuthoringSessionResult gapped = await coordinator.ApplyDeltaAsync(new LiveAuthoringDelta(
            epoch,
            9,
            [
                new SetAttributeUpdate(
                    $"{bridgeRoot}/Cube",
                    "custom:pressure",
                    LiveAttributeValue.FromDouble(9.5))
            ],
            originId: "kit-bridge"));

        bool loopbackEnforced;
        try
        {
            new BridgeClientOptions
            {
                Endpoint = new Uri("http://bridge.example.com:8080"),
                Credentials = new EphemeralBearerTokenProvider(
                    "token",
                    DateTimeOffset.UtcNow.AddMinutes(5)),
                BridgeRootPath = bridgeRoot,
                LocalOriginId = localOrigin
            }.Validate();
            loopbackEnforced = false;
        }
        catch (ArgumentException)
        {
            loopbackEnforced = true;
        }

        var credential = new BridgeCallCredential("secret-token", DateTimeOffset.UtcNow.AddMinutes(5));
        bool credentialRedacted = !credential.ToString().Contains("secret-token", StringComparison.Ordinal);

        var options = new BridgeClientOptions
        {
            Endpoint = new Uri("http://127.0.0.1:53017"),
            Credentials = new EphemeralBearerTokenProvider(
                "token",
                DateTimeOffset.UtcNow.AddMinutes(5)),
            BridgeRootPath = bridgeRoot

            // LocalOriginId deliberately unset: the client must adopt the coordinator's resolved
            // identity, which is what keeps echo suppression and idempotency keys consistent.
        };
        await using var client = new OmniverseBridgeClient(coordinator, options);
        BridgeClientStatus status = client.GetStatus();
        bool originAdopted = client.LocalOriginId == coordinator.LocalOriginId;

        await using var generatedSink = new QueuedLiveAuthoringSink(new OverlayExecutor(), capacity: 4);
        await using var generatedCoordinator = new LiveAuthoringSessionCoordinator(
            generatedSink,
            new LiveAuthoringSessionOptions { BridgeRootPath = bridgeRoot });
        bool generatedOriginIsUnique =
            generatedCoordinator.LocalOriginId != localOrigin &&
            generatedCoordinator.LocalOriginId != LiveAuthoringOriginId.CreateProcessInstanceUnique() &&
            LiveAuthoringValidation.IsWellFormedUtf16(generatedCoordinator.LocalOriginId);

        bool illFormedOriginRejected;
        try
        {
            _ = new LiveAuthoringRemoteEpoch("origin-\ud800", "session-a", 1);
            illFormedOriginRejected = false;
        }
        catch (ArgumentException)
        {
            illFormedOriginRejected = true;
        }

        Console.WriteLine($"BRIDGE_PROTOCOL={BridgeProtocol.PackageName}");
        Console.WriteLine($"BRIDGE_VERSION={BridgeProtocol.Version}");
        Console.WriteLine($"BRIDGE_SERVICE={BridgeGrpcProtocol.ServiceName}");
        Console.WriteLine($"DELTA_ROUNDTRIP={deltaRoundTrip}");
        Console.WriteLine($"DECODE_ERROR={decodeError.Code}");
        Console.WriteLine($"SNAPSHOT_DECODED={snapshotDecoded}");
        Console.WriteLine($"SNAPSHOT_APPLIED={applied.IsApplied}");
        Console.WriteLine($"DELTA_APPLIED={deltaApplied.IsApplied}");
        Console.WriteLine(
            $"GAP_REQUIRES_RESYNC={gapped.Rejection == LiveAuthoringSessionRejection.SequenceGap}");
        Console.WriteLine($"MALFORMED_REJECTED={malformedRejected}");
        Console.WriteLine($"LOOPBACK_ONLY_ENFORCED={loopbackEnforced}");
        Console.WriteLine($"CREDENTIAL_REDACTED={credentialRedacted}");
        Console.WriteLine($"DESCRIPTOR_BYTES={BridgeProtocol.CreateDescriptorSet().Length}");
        Console.WriteLine($"SERVICE_DESCRIPTOR_BYTES={BridgeGrpcProtocol.CreateDescriptorSet().Length}");
        Console.WriteLine($"CLIENT_STATE={status.State}");
        Console.WriteLine($"ORIGIN_ADOPTED={originAdopted}");
        Console.WriteLine($"GENERATED_ORIGIN_UNIQUE={generatedOriginIsUnique}");
        Console.WriteLine($"ILL_FORMED_ORIGIN_REJECTED={illFormedOriginRejected}");
        Console.WriteLine(
            $"INDETERMINATE_OUTCOME={BridgeLocalPublicationOutcome.Indeterminate}");
        Console.WriteLine($"NATIVE_AOT={!RuntimeFeature.IsDynamicCodeSupported}");
        return deltaRoundTrip &&
            snapshotDecoded &&
            applied.IsApplied &&
            deltaApplied.IsApplied &&
            gapped.Rejection == LiveAuthoringSessionRejection.SequenceGap &&
            malformedRejected &&
            loopbackEnforced &&
            originAdopted &&
            generatedOriginIsUnique &&
            illFormedOriginRejected &&
            credentialRedacted ? 0 : 1;

        sealed class OverlayExecutor : ILiveAuthoringBatchExecutor
        {
            private ulong _serial;

            public ValueTask<LiveAuthoringBatchResult> ExecuteAsync(
                LiveAuthoringBatch batch,
                CancellationToken cancellationToken)
            {
                ulong before = _serial++;
                return ValueTask.FromResult(new LiveAuthoringBatchResult(
                    batch.Sequence,
                    batch.Sequence,
                    1,
                    batch.Updates.Count,
                    batch.Invalidation,
                    before,
                    _serial,
                    "memory://session",
                    batch.CorrelationId,
                    batch.OriginId));
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
        """;

    private static bool IsMissingAotToolchain(string output) =>
        output.Contains("Platform linker not found", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("requires the Visual C++", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("vswhere.exe' is not recognized", StringComparison.OrdinalIgnoreCase) ||
        (output.Contains("Microsoft.NETCore.Native", StringComparison.OrdinalIgnoreCase) &&
            output.Contains("could not be found", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns the environment additions the native AOT link step needs on this host.
    /// </summary>
    /// <remarks>
    /// The ILCompiler link step shells out to <c>vswhere.exe</c> to locate the Visual C++ linker.
    /// A developer machine that has the toolset installed does not always have the installer
    /// directory on PATH, and the resulting failure looks like a product defect rather than an
    /// environment gap. Adding the directory when it exists keeps the gate meaningful; when it does
    /// not exist the publish fails and the test reports an absent prerequisite instead.
    /// </remarks>
    private static Dictionary<string, string> CreateNativeToolchainEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!OperatingSystem.IsWindows())
        {
            return environment;
        }

        string installerRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio",
            "Installer");
        if (!File.Exists(Path.Combine(installerRoot, "vswhere.exe")))
        {
            return environment;
        }

        string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (!path.Contains(installerRoot, StringComparison.OrdinalIgnoreCase))
        {
            environment["PATH"] = $"{installerRoot}{Path.PathSeparator}{path}";
        }

        return environment;
    }

    private static void HandleMissingExecutionPrerequisites(string testName, string reason)
    {
        string message = $"{testName} did not run because {reason}.";
        if (string.Equals(
            Environment.GetEnvironmentVariable(RequiredExecutionEnvironmentVariable),
            "true",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{message} {RequiredExecutionEnvironmentVariable}=true requires execution.");
        }

        Console.WriteLine($"PACKAGE_EXECUTION_PREREQUISITES_ABSENT: {message}");
    }

    private static async Task<PackedPackage> PackAsync(
        string repositoryRoot,
        string packageId,
        string packageRoot)
    {
        string projectPath = Path.Combine(repositoryRoot, "src", packageId, $"{packageId}.csproj");
        CommandResult result = await RunDotnetAsync(
            repositoryRoot,
            [
                "pack",
                projectPath,
                "-c",
                "Release",
                "--nologo",
                "-p:BuildInParallel=false",
                $"-p:PackageOutputPath={packageRoot}"
            ]);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.Output);
        }

        string packagePath = Directory
            .GetFiles(packageRoot, $"{packageId}.*.nupkg")
            .Where(path => !path.EndsWith(".symbols.nupkg", StringComparison.Ordinal))
            .Single(path => string.Equals(ReadPackageId(path), packageId, StringComparison.Ordinal));
        return new PackedPackage(packagePath, ReadPackageVersion(packagePath));
    }

    private static async Task AssertPackageDependsOnAsync(string packagePath, string dependencyId)
    {
        string nuspec = await ReadNuspecAsync(packagePath);
        await Assert.That(nuspec).Contains($"id=\"{dependencyId}\"");
    }

    private static async Task AssertPackageDoesNotDependOnAsync(string packagePath, string dependencyId)
    {
        string nuspec = await ReadNuspecAsync(packagePath);
        await Assert.That(nuspec).DoesNotContain($"id=\"{dependencyId}\"");
    }

    private static async Task<string> ReadNuspecAsync(string packagePath)
    {
        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry entry = archive.Entries.Single(candidate =>
            candidate.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        using StreamReader reader = new(entry.Open());
        return await reader.ReadToEndAsync();
    }

    private static string ReadPackageId(string packagePath) =>
        ReadNuspecElement(packagePath, "id");

    private static string ReadPackageVersion(string packagePath) =>
        ReadNuspecElement(packagePath, "version");

    private static string ReadNuspecElement(string packagePath, string elementName)
    {
        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry entry = archive.Entries.Single(candidate =>
            candidate.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        using Stream stream = entry.Open();
        XDocument nuspec = XDocument.Load(stream);
        XNamespace ns = nuspec.Root!.GetDefaultNamespace();
        return nuspec.Root!.Element(ns + "metadata")!.Element(ns + elementName)!.Value;
    }

    private static void AssertPackageOnlyGraph(
        string assetsPath,
        IReadOnlyCollection<string> expectedPackageIds)
    {
        using JsonDocument assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        JsonElement libraries = assets.RootElement.GetProperty("libraries");
        var expected = new HashSet<string>(expectedPackageIds, StringComparer.Ordinal);
        foreach (JsonProperty library in libraries.EnumerateObject())
        {
            string type = library.Value.GetProperty("type").GetString()
                ?? throw new InvalidOperationException($"Package type is missing for {library.Name}.");
            if (string.Equals(type, "project", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{library.Name} restored from a project.");
            }

            expected.Remove(library.Name.Split('/')[0]);
        }

        if (expected.Count != 0)
        {
            throw new InvalidOperationException(
                $"The package graph is missing: {string.Join(", ", expected)}.");
        }
    }

    private static string CreateWorkRoot(string repositoryRoot)
    {
        string workRoot = Path.Combine(
            repositoryRoot,
            "artifacts",
            "bridge-package-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);
        return workRoot;
    }

    private static async Task<CommandResult> RunDotnetAsync(
        string workingDirectory,
        string[] arguments,
        IReadOnlyDictionary<string, string>? environment = null) =>
        await RunProcessAsync("dotnet", arguments, workingDirectory, environment);

    private static async Task<CommandResult> RunExecutableAsync(
        string executablePath,
        string workingDirectory) =>
        await RunProcessAsync(executablePath, [], workingDirectory);

    private static async Task<CommandResult> RunProcessAsync(
        string fileName,
        string[] arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        if (environment is not null)
        {
            foreach (KeyValuePair<string, string> entry in environment)
            {
                startInfo.Environment[entry.Key] = entry.Value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new CommandResult(
            process.ExitCode,
            await standardOutput + await standardError);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new InvalidOperationException("The repository root could not be located.");
    }

    private sealed record PackedPackage(string Path, string Version);

    private sealed record CommandResult(int ExitCode, string Output);
}
