// Copyright (c) marcschier. Licensed under the MIT License.

using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;

namespace OpenUsd.Mcp.Tests;

[NotInParallel]
public sealed class McpHostConfigurationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task HostDoesNotWatchTheWorkingTreeUnlessExplicitlyRequested(bool watch)
    {
        const string variable = "DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE";
        string? previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, watch ? "true" : null);
            using var files = new WorkspaceTestFiles();
            await File.WriteAllTextAsync(Path.Combine(files.SourceRoot, "appsettings.json"),
                """{"McpProbe":{"Entry":"original"}}""");
            var builder = McpHostConfiguration.CreateHostBuilder(["--contentRoot", files.SourceRoot]);
            using var host = builder.Build();
            var configuration = builder.Configuration;
            JsonConfigurationSource[] sources = configuration.Sources.OfType<JsonConfigurationSource>().ToArray();
            await Assert.That(sources).IsNotEmpty();
            await Assert.That(configuration["hostBuilder:reloadConfigOnChange"])
                .IsEqualTo(watch ? "true" : "false");
            await Assert.That(sources.All(source => source.ReloadOnChange == watch)).IsTrue()
                .Because(string.Join(", ", sources.Select(source => $"{source.Path}: {source.ReloadOnChange}")));
            await Assert.That(configuration["McpProbe:Entry"]).IsEqualTo("original");
            await Assert.That(builder.Environment.ContentRootPath).IsEqualTo(files.SourceRoot);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Test]
    public async Task GpuUploadConfigurationRequiresExplicitHardwareAndBufferAdmission()
    {
        string[] variables = ["OPENUSD_D3D12_GPU_UPLOAD_HEAPS", "OPENUSD_MCP_USE_WARP",
            "OPENUSD_SILK_GPU_BUFFER_BYTES"];
        string?[] previous = variables.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            foreach (string variable in variables)
            {
                Environment.SetEnvironmentVariable(variable, null);
            }
            await Assert.That(McpHostConfiguration.LoadOptions().Graphics!.D3D12GpuUploadHeaps).IsFalse();
            Environment.SetEnvironmentVariable(variables[0], "true");
            OpenUsdMcpApplicationOptions options = McpHostConfiguration.LoadOptions();
            await Assert.That(options.Graphics!.D3D12GpuUploadHeaps).IsTrue();
            await Assert.That(() => new ServiceCollection().AddOpenUsdMcpServices(options))
                .Throws<ArgumentException>();
            Environment.SetEnvironmentVariable(variables[1], "false");
            options = McpHostConfiguration.LoadOptions();
            await Assert.That(() => new ServiceCollection().AddOpenUsdMcpServices(options))
                .Throws<ArgumentException>();
            Environment.SetEnvironmentVariable(variables[2], "1048576");
            options = McpHostConfiguration.LoadOptions();
            if (OperatingSystem.IsWindows())
            {
                var services = new ServiceCollection();
                await Assert.That(services.AddOpenUsdMcpServices(options)).IsSameReferenceAs(services);
            }
            else
            {
                await Assert.That(() => new ServiceCollection().AddOpenUsdMcpServices(options))
                    .Throws<ArgumentException>();
            }
            Environment.SetEnvironmentVariable(variables[0], "false");
            await Assert.That(McpHostConfiguration.LoadOptions().Graphics!.D3D12GpuUploadHeaps).IsFalse();
            Environment.SetEnvironmentVariable(variables[0], "invalid");
            await Assert.That(McpHostConfiguration.LoadOptions).Throws<ArgumentException>();
        }
        finally
        {
            for (int index = 0; index < variables.Length; index++)
            {
                Environment.SetEnvironmentVariable(variables[index], previous[index]);
            }
        }
    }

    [Test]
    public async Task StreamingConfigurationIsExplicitAndServicesRejectMissingBufferAdmission()
    {
        const string variable = "OPENUSD_SILK_STREAM_MATERIAL_TEXTURES";
        string? previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, null);
            await Assert.That(McpHostConfiguration.LoadOptions().StreamMaterialTextures).IsFalse();
            Environment.SetEnvironmentVariable(variable, "true");
            OpenUsdMcpApplicationOptions options = McpHostConfiguration.LoadOptions();
            await Assert.That(options.StreamMaterialTextures).IsTrue();
            await Assert.That(() => new ServiceCollection().AddOpenUsdMcpServices(
                options with { GpuBufferBudget = null })).Throws<ArgumentException>();
            Environment.SetEnvironmentVariable(variable, "false");
            await Assert.That(McpHostConfiguration.LoadOptions().StreamMaterialTextures).IsFalse();
            Environment.SetEnvironmentVariable(variable, "invalid");
            await Assert.That(McpHostConfiguration.LoadOptions).Throws<ArgumentException>();
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Test]
    public async Task StoredCommandPolicyRequiresPreparationAndPreservesAllCeilings()
    {
        string[] variables = ["OPENUSD_SILK_MESH_RESERVATION_BYTES", "OPENUSD_SILK_MAX_PAGE_BYTES",
            "OPENUSD_SILK_STORAGE_LIVE_BYTES", "OPENUSD_SILK_STORAGE_UPDATE_BYTES",
            "OPENUSD_SILK_STORAGE_COMMAND_BYTES", "OPENUSD_SILK_STORAGE_TRANSFER_BYTES"];
        string?[] before = variables.Select(Environment.GetEnvironmentVariable).ToArray();
        string[] values = ["1000000", "65536", "6000000000", "5000000000", "32768", "4096"];
        try
        {
            for (int index = 0; index < variables.Length; index++)
            {
                Environment.SetEnvironmentVariable(variables[index], values[index]);
            }
            var storage = McpHostConfiguration.LoadOptions().PreparationLimits!.CommandStorage!;
            await Assert.That(storage.MaximumLiveBytes).IsEqualTo(6000000000ul);
            await Assert.That(storage.MaximumUpdateBytes).IsEqualTo(5000000000ul);
            await Assert.That(storage.MaximumCommandBytes).IsEqualTo(32768);
            await Assert.That(storage.TransferBufferBytes).IsEqualTo(4096);
            Environment.SetEnvironmentVariable(variables[5], null);
            await Assert.That(McpHostConfiguration.LoadOptions).Throws<ArgumentException>();
            Environment.SetEnvironmentVariable(variables[5], values[5]);
            Environment.SetEnvironmentVariable(variables[0], null);
            Environment.SetEnvironmentVariable(variables[1], null);
            await Assert.That(McpHostConfiguration.LoadOptions).Throws<ArgumentException>();
        }
        finally
        {
            for (int index = 0; index < variables.Length; index++)
            {
                Environment.SetEnvironmentVariable(variables[index], before[index]);
            }
        }
    }

    [Test]
    public async Task TexturePolicyDomainsAreIndependentOptionalAndStrict()
    {
        string[] variables =
            ["OPENUSD_SILK_CPU_TEXTURE_BYTES", "OPENUSD_SILK_GPU_TEXTURE_BYTES", "OPENUSD_SILK_GPU_STAGING_BYTES"];
        string?[] previous = variables.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            foreach (string variable in variables)
            {
                Environment.SetEnvironmentVariable(variable, null);
            }
            await Assert.That(McpHostConfiguration.LoadOptions().TexturePreparationBudgets).IsNull();
            for (int domain = 0; domain < variables.Length; domain++)
            {
                Environment.SetEnvironmentVariable(variables[domain], "5000");
                OpenUsd.Rendering.Silk.SilkTexturePreparationBudgets budgets =
                    McpHostConfiguration.LoadOptions().TexturePreparationBudgets!;
                await Assert.That(budgets.CpuTextureBudget?.MaximumBytes)
                    .IsEqualTo(domain == 0 ? 5000ul : (ulong?)null);
                await Assert.That(budgets.GpuTextureBudget?.MaximumBytes)
                    .IsEqualTo(domain == 1 ? 5000ul : (ulong?)null);
                await Assert.That(budgets.GpuStagingBudget?.MaximumBytes)
                    .IsEqualTo(domain == 2 ? 5000ul : (ulong?)null);
                Environment.SetEnvironmentVariable(variables[domain], "0");
                await Assert.That(McpHostConfiguration.LoadOptions).Throws<ArgumentException>();
                Environment.SetEnvironmentVariable(variables[domain], null);
            }
        }
        finally
        {
            for (int index = 0; index < variables.Length; index++)
            {
                Environment.SetEnvironmentVariable(variables[index], previous[index]);
            }
        }
    }

    [Test]
    public async Task GpuBufferBudgetIsIndependentOptionalAndStrictlyValidated()
    {
        const string variable = "OPENUSD_SILK_GPU_BUFFER_BYTES";
        string? previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, null);
            await Assert.That(McpHostConfiguration.LoadOptions().GpuBufferBudget).IsNull();
            Environment.SetEnvironmentVariable(variable, "5000");
            await Assert.That(McpHostConfiguration.LoadOptions().GpuBufferBudget!.MaximumBytes).IsEqualTo(5000ul);
            Environment.SetEnvironmentVariable(variable, "0");
            await Assert.That(McpHostConfiguration.LoadOptions).Throws<ArgumentException>();
            Environment.SetEnvironmentVariable(variable, "1.5");
            await Assert.That(McpHostConfiguration.LoadOptions).Throws<ArgumentException>();
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Test]
    public async Task SessionPreparationConfigurationIsOptionalStrictAndUsesExactByteLimits()
    {
        const string meshVariable = "OPENUSD_SILK_MESH_RESERVATION_BYTES";
        const string pageVariable = "OPENUSD_SILK_MAX_PAGE_BYTES";
        string? meshBefore = Environment.GetEnvironmentVariable(meshVariable);
        string? pageBefore = Environment.GetEnvironmentVariable(pageVariable);
        try
        {
            Environment.SetEnvironmentVariable(meshVariable, null);
            Environment.SetEnvironmentVariable(pageVariable, null);
            await Assert.That(McpHostConfiguration.LoadOptions().PreparationLimits).IsNull();
            Environment.SetEnvironmentVariable(meshVariable, "5000");
            await Assert.That(McpHostConfiguration.LoadOptions).Throws<ArgumentException>();
            Environment.SetEnvironmentVariable(pageVariable, "6000");
            OpenUsd.Rendering.Silk.SilkPreparationLimits limits = McpHostConfiguration.LoadOptions().PreparationLimits!;
            await Assert.That(limits.MaximumMeshPreparationReservationBytes).IsEqualTo(5000ul);
            await Assert.That(limits.MaximumCommandPageBytes).IsEqualTo(6000);
            Environment.SetEnvironmentVariable(pageVariable, "0");
            await Assert.That(McpHostConfiguration.LoadOptions).Throws<ArgumentException>();
        }
        finally
        {
            Environment.SetEnvironmentVariable(meshVariable, meshBefore);
            Environment.SetEnvironmentVariable(pageVariable, pageBefore);
        }
    }

    [Test]
    [Arguments(null, true)]
    [Arguments("true", true)]
    [Arguments("false", false)]
    [Arguments("FALSE", false)]
    public async Task WindowsPreviewDevicePreferencePreservesDefaultAndAllowsExplicitHardware(
        string? configured, bool expectedWarp)
    {
        const string variable = "OPENUSD_MCP_USE_WARP";
        string? previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, configured);
            OpenUsdMcpApplicationOptions options = McpHostConfiguration.LoadOptions();
            PreviewGraphicsDeviceOptions graphics = options.Graphics ?? new PreviewGraphicsDeviceOptions();
            await Assert.That(graphics.UseWarpOnWindows).IsEqualTo(expectedWarp);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Test]
    [Arguments("hardware")]
    [Arguments("1")]
    [Arguments("unknown")]
    public async Task InvalidPreviewDevicePreferenceDoesNotSilentlySelectAnyRenderer(string configured)
    {
        const string variable = "OPENUSD_MCP_USE_WARP";
        string? previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, configured);
            await Assert.That(McpHostConfiguration.LoadOptions).Throws<ArgumentException>();
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Test]
    public async Task ViewerPathDefaultsBeneathConfiguredViewerRoot()
    {
        string root = Path.Combine(
            AppContext.BaseDirectory,
            "configuration-tests",
            Guid.NewGuid().ToString("N"));
        string sourceRoot = Path.Combine(root, "source");
        string outputRoot = Path.Combine(root, "output");
        string viewerRoot = Path.Combine(root, "viewer");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(viewerRoot);
        string? originalSource = Environment.GetEnvironmentVariable(
            "OPENUSD_MCP_SOURCE_ROOT");
        string? originalOutput = Environment.GetEnvironmentVariable(
            "OPENUSD_MCP_OUTPUT_ROOT");
        string? originalViewerRoot = Environment.GetEnvironmentVariable(
            "OPENUSD_MCP_VIEWER_ROOT");
        string? originalViewerPath = Environment.GetEnvironmentVariable(
            "OPENUSD_MCP_VIEWER_PATH");
        string? originalArtifactStoreBytes = Environment.GetEnvironmentVariable(
            "OPENUSD_MCP_MAX_ARTIFACT_STORE_BYTES");
        string? originalArtifactReadBytes = Environment.GetEnvironmentVariable(
            "OPENUSD_MCP_MAX_ARTIFACT_READ_BYTES");
        try
        {
            Environment.SetEnvironmentVariable("OPENUSD_MCP_SOURCE_ROOT", sourceRoot);
            Environment.SetEnvironmentVariable("OPENUSD_MCP_OUTPUT_ROOT", outputRoot);
            Environment.SetEnvironmentVariable("OPENUSD_MCP_VIEWER_ROOT", viewerRoot);
            Environment.SetEnvironmentVariable("OPENUSD_MCP_VIEWER_PATH", null);
            Environment.SetEnvironmentVariable(
                "OPENUSD_MCP_MAX_ARTIFACT_STORE_BYTES",
                "12345");
            Environment.SetEnvironmentVariable(
                "OPENUSD_MCP_MAX_ARTIFACT_READ_BYTES",
                "6789");

            OpenUsdMcpApplicationOptions options = McpHostConfiguration.LoadOptions();

            await Assert.That(options.ViewerExecutableRoot)
                .IsEqualTo(Path.GetFullPath(viewerRoot));
            await Assert.That(options.ViewerExecutablePath)
                .IsEqualTo(Path.Combine(
                    Path.GetFullPath(viewerRoot),
                    OperatingSystem.IsWindows()
                        ? "OpenUsd.Viewer.App.exe"
                        : "OpenUsd.Viewer.App"));
            await Assert.That(options.ArtifactStore!.MaximumTotalBytes)
                .IsEqualTo(12345);
            await Assert.That(options.ArtifactStore.MaximumReadResponseBytes)
                .IsEqualTo(6789);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENUSD_MCP_SOURCE_ROOT", originalSource);
            Environment.SetEnvironmentVariable("OPENUSD_MCP_OUTPUT_ROOT", originalOutput);
            Environment.SetEnvironmentVariable(
                "OPENUSD_MCP_VIEWER_ROOT",
                originalViewerRoot);
            Environment.SetEnvironmentVariable(
                "OPENUSD_MCP_VIEWER_PATH",
                originalViewerPath);
            Environment.SetEnvironmentVariable(
                "OPENUSD_MCP_MAX_ARTIFACT_STORE_BYTES",
                originalArtifactStoreBytes);
            Environment.SetEnvironmentVariable(
                "OPENUSD_MCP_MAX_ARTIFACT_READ_BYTES",
                originalArtifactReadBytes);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
