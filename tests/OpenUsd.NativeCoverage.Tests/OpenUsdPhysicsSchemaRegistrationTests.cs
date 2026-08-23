// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using OpenUsd.Physics.Schema;

namespace OpenUsd.NativeCoverage.Tests;

/// <summary>
/// Runs the <c>openUsdPhysics</c> codeless schema probes in a clean child process and
/// fails with the child's output when they do not pass.
/// </summary>
/// <remarks>
/// <para>
/// OpenUSD builds its schema registry once, the first time anything asks for it, from
/// whichever plugins are known at that moment. Registering a plugin after that point
/// still reports success, but its schema types never enter the registry and prims come
/// back with no fallback properties — so a probe running in the shared test host passes
/// or fails according to whatever touched a stage before it.
/// </para>
/// <para>
/// Setting <c>PXR_PLUGINPATH_NAME</c> from inside the process does not help either: on
/// Windows <see cref="Environment.SetEnvironmentVariable(string, string)"/> updates only
/// the managed environment block, and the native runtime reads the real one.
/// </para>
/// <para>
/// The probes therefore need their own process, started with the plugin root already on
/// the search path. This test provides it; <see cref="OpenUsdPhysicsSchemaProbeTests"/>
/// holds the assertions that run there.
/// </para>
/// </remarks>
public sealed class OpenUsdPhysicsSchemaRegistrationTests
{
    [Test]
    public async Task CleanProcessProbeLoadsTheCodelessPluginAndResolvesItsTypes()
    {
        if (OpenUsdPhysicsSchemaProbeTests.IsProbeProcess)
        {
            return;
        }

        string resources = OpenUsdPhysicsSchemaProbeTests.ProjectSchemaResources();
        await Assert.That(File.Exists(Path.Combine(resources, "plugInfo.json"))).IsTrue()
            .Because("regenerate the plugin with schemas/openUsdPhysics/tools/generate_schema.py.");
        await Assert.That(File.Exists(Path.Combine(resources, "generatedSchema.usda"))).IsTrue();

        // Confirm the staged runtime this process depends on actually loads, so a staging
        // problem is not reported as a schema failure in the child.
        NativeCoverageRuntime.EnsureNativeLoaded();

        (int exitCode, string output) = RunProbeProcess(resources);

        await Assert.That(exitCode).IsEqualTo(0)
            .Because($"the clean-process schema probe failed.{Environment.NewLine}{output}");
    }

    [Test]
    public async Task CleanProcessProbeLoadsThePluginEmbeddedInOpenUsdPhysics()
    {
        if (OpenUsdPhysicsSchemaProbeTests.IsProbeProcess)
        {
            return;
        }

        NativeCoverageRuntime.EnsureNativeLoaded();

        string root = NativeCoverageRuntime.CreateTempDirectory(
            nameof(CleanProcessProbeLoadsThePluginEmbeddedInOpenUsdPhysics));
        string resources = OpenUsdPhysicsSchemaResources.ExtractPluginTo(root);

        await Assert.That(File.Exists(Path.Combine(resources, "plugInfo.json"))).IsTrue()
            .Because("OpenUsd.Physics must embed the codeless plugin registration.");
        await Assert.That(File.Exists(Path.Combine(resources, "generatedSchema.usda"))).IsTrue();

        (int exitCode, string output) = RunProbeProcess(resources);

        await Assert.That(exitCode).IsEqualTo(0)
            .Because("the plugin extracted from OpenUsd.Physics must register the same way the "
                + $"repository copy does.{Environment.NewLine}{output}");
    }

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
        startInfo.ArgumentList.Add($"/*/*/{nameof(OpenUsdPhysicsSchemaProbeTests)}/*");

        string? existing = Environment.GetEnvironmentVariable("PXR_PLUGINPATH_NAME");
        startInfo.Environment["PXR_PLUGINPATH_NAME"] = string.IsNullOrEmpty(existing)
            ? resources
            : existing + Path.PathSeparator + resources;
        startInfo.Environment[OpenUsdPhysicsSchemaProbeTests.ProbeVariable] = "1";

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"Unable to start the schema probe process '{startInfo.FileName}'.");

        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(180_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The clean-process schema probe did not finish within 180 seconds.");
        }

        return (process.ExitCode, standardOutput + standardError);
    }
}
