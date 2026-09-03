// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;

namespace OpenUsd.NativeCoverage.Tests;

/// <summary>
/// Runs <see cref="ExternalSchemaFixtureProbeTests"/> in a clean child process with the
/// synthetic external schema fixture's resources directory on <c>PXR_PLUGINPATH_NAME</c> before
/// OpenUSD builds its schema registry, and fails with the child's output when they do not pass.
/// </summary>
/// <remarks>
/// OpenUSD builds its schema registry once, the first time anything asks for it. A plugin
/// registered afterwards still reports success but contributes no schema types, so this probe
/// needs its own process started with the plugin root already on the search path -- the same
/// reason <c>OpenUsdPhysicsSchemaRegistrationTests</c> exists for the project-owned
/// <c>openUsdPhysics</c> plugin. This test proves the same registration contract holds for a
/// codeless plugin tree supplied from outside this repository's own <c>schemas/</c> directory.
/// </remarks>
public sealed class ExternalSchemaFixtureRegistrationTests
{
    [Test]
    public async Task CleanProcessProbeLoadsTheExternalCodelessPluginAndResolvesItsTypes()
    {
        if (ExternalSchemaFixtureProbeTests.IsProbeProcess)
        {
            return;
        }

        string resources = FixtureResources();
        await Assert.That(File.Exists(Path.Combine(resources, "plugInfo.json"))).IsTrue()
            .Because("the synthetic external schema fixture must ship its plugInfo.json.");
        await Assert.That(File.Exists(Path.Combine(resources, "generatedSchema.usda"))).IsTrue();

        // Confirm the staged runtime this process depends on actually loads, so a staging
        // problem is not reported as a schema failure in the child.
        NativeCoverageRuntime.EnsureNativeLoaded();

        (int exitCode, string output) = RunProbeProcess(resources);

        await Assert.That(exitCode).IsEqualTo(0)
            .Because($"the clean-process external schema probe failed.{Environment.NewLine}{output}");
    }

    internal static string FixtureResources() => Path.Combine(
        FindRepositoryRoot(), "test-assets", "omniverse", "external-schema", "resources");

    private static (int ExitCode, string Output) RunProbeProcess(string resources)
    {
        ProcessStartInfo startInfo = new()
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory,
        };

        string? host = Environment.ProcessPath;
        bool hostIsMuxer = host is null
            || Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        startInfo.FileName = host ?? "dotnet";
        if (hostIsMuxer)
        {
            startInfo.ArgumentList.Add(
                Path.Combine(AppContext.BaseDirectory, "OpenUsd.NativeCoverage.Tests.dll"));
        }

        startInfo.ArgumentList.Add("--treenode-filter");
        startInfo.ArgumentList.Add($"/*/*/{nameof(ExternalSchemaFixtureProbeTests)}/*");

        string? existing = Environment.GetEnvironmentVariable("PXR_PLUGINPATH_NAME");
        startInfo.Environment["PXR_PLUGINPATH_NAME"] = string.IsNullOrEmpty(existing)
            ? resources
            : existing + Path.PathSeparator + resources;
        startInfo.Environment[ExternalSchemaFixtureProbeTests.ProbeVariable] = "1";

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"Unable to start the external schema probe process '{startInfo.FileName}'.");

        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(180_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The clean-process external schema probe did not finish within 180 seconds.");
        }

        return (process.ExitCode, standardOutput + standardError);
    }

    private static string FindRepositoryRoot()
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

        throw new InvalidOperationException(
            $"Unable to locate the repository root from '{AppContext.BaseDirectory}'.");
    }
}
