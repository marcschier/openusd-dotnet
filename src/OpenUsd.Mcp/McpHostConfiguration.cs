// Copyright (c) marcschier. Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OpenUsd.Mcp;

internal sealed record OpenUsdMcpApplicationOptions(
    string SourceRoot,
    string OutputRoot,
    string PluginPath,
    string ViewerExecutableRoot,
    string ViewerExecutablePath,
    int MaximumBatchOperationCount = OpenUsdMcpLimits.MaximumEditCount,
    int CaptureQueueCapacity = 8,
    ArtifactResourceStoreOptions? ArtifactStore = null,
    PreviewCaptureLimits? CaptureLimits = null,
    PreviewGraphicsDeviceOptions? Graphics = null,
    OpenUsdMcpProtocolOptions? Protocol = null,
    int MaximumCheckpointCount = 256,
    int MaximumJournalEntryCount = 1024,
    int MaximumAppliedProposalHistoryCount = 1024);

internal static class McpHostConfiguration
{
    internal static void ConfigureLogging(ILoggingBuilder logging)
    {
        ArgumentNullException.ThrowIfNull(logging);
        logging.ClearProviders();
        logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });
    }

    internal static OpenUsdMcpApplicationOptions LoadOptions()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string viewerName = OperatingSystem.IsWindows()
            ? "OpenUsd.Viewer.App.exe"
            : "OpenUsd.Viewer.App";
        string viewerRoot = GetPath("OPENUSD_MCP_VIEWER_ROOT", baseDirectory);
        string outputRoot = GetPath(
            "OPENUSD_MCP_OUTPUT_ROOT",
            Path.Combine(baseDirectory, "openusd-mcp-output"));
        return new OpenUsdMcpApplicationOptions(
            GetPath("OPENUSD_MCP_SOURCE_ROOT", Environment.CurrentDirectory),
            outputRoot,
            Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH") ?? string.Empty,
            viewerRoot,
            GetPath(
                "OPENUSD_MCP_VIEWER_PATH",
                Path.Combine(viewerRoot, viewerName)),
            ArtifactStore: new ArtifactResourceStoreOptions(
                MaximumTotalBytes: GetPositiveLong(
                    "OPENUSD_MCP_MAX_ARTIFACT_STORE_BYTES",
                    64L * 1024 * 1024),
                MaximumReadResponseBytes: GetPositiveLong(
                    "OPENUSD_MCP_MAX_ARTIFACT_READ_BYTES",
                    64L * 1024 * 1024)),
            MaximumCheckpointCount: GetNonNegativeInt(
                "OPENUSD_MCP_MAX_CHECKPOINTS",
                256),
            MaximumJournalEntryCount: GetPositiveInt(
                "OPENUSD_MCP_MAX_JOURNAL_ENTRIES",
                1024),
            MaximumAppliedProposalHistoryCount: GetNonNegativeInt(
                "OPENUSD_MCP_MAX_APPLIED_PROPOSALS",
                1024));
    }

    internal static IServiceCollection AddOpenUsdMcpServices(
        this IServiceCollection services,
        OpenUsdMcpApplicationOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumBatchOperationCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.CaptureQueueCapacity);
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaximumCheckpointCount);
        if (options.MaximumJournalEntryCount < McpSessionWorkspace.MinimumJournalEntryCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaximumJournalEntryCount,
                $"The journal quota must be at least {McpSessionWorkspace.MinimumJournalEntryCount}.");
        }
        ArgumentOutOfRangeException.ThrowIfNegative(
            options.MaximumAppliedProposalHistoryCount);

        ArtifactResourceStoreOptions artifactOptions =
            options.ArtifactStore ?? new ArtifactResourceStoreOptions();
        artifactOptions = artifactOptions with
        {
            FileStorageRoot = artifactOptions.FileStorageRoot ??
                Path.Combine(options.OutputRoot, ".artifact-resources"),
        };
        PreviewCaptureLimits captureLimits =
            options.CaptureLimits ?? new PreviewCaptureLimits();
        PreviewGraphicsDeviceOptions graphicsOptions =
            options.Graphics ?? new PreviewGraphicsDeviceOptions();
        OpenUsdMcpProtocolOptions protocolOptions =
            options.Protocol ?? new OpenUsdMcpProtocolOptions(
                InlineImageMaximumBytes: artifactOptions.InlineThresholdBytes);

        services.AddSingleton(options);
        services.AddSingleton(protocolOptions);
        services.AddSingleton<IArtifactResourceStore>(
            _ => new ArtifactResourceStore(artifactOptions));
        services.AddSingleton(
            _ => new McpSessionWorkspace(
                new McpSessionWorkspaceOptions(
                    options.SourceRoot,
                    options.OutputRoot,
                    options.MaximumBatchOperationCount,
                    options.MaximumCheckpointCount,
                    options.MaximumJournalEntryCount)));
        services.AddSingleton<IPreviewGraphicsDeviceFactory, PreviewGraphicsDeviceFactory>();
        services.AddSingleton<IPreviewFrameSourceFactory>(provider =>
            new PreviewSilkFrameSourceFactory(
                options.PluginPath,
                provider.GetRequiredService<McpSessionWorkspace>(),
                provider.GetRequiredService<IPreviewGraphicsDeviceFactory>(),
                graphicsOptions));
        services.AddSingleton<IPreviewCaptureProcessor>(provider =>
            new PreviewCaptureProcessor(
                provider.GetRequiredService<IPreviewFrameSourceFactory>(),
                provider.GetRequiredService<IArtifactResourceStore>(),
                captureLimits));
        services.AddSingleton(provider =>
            new CaptureWorker(
                provider.GetRequiredService<IPreviewCaptureProcessor>(),
                options.CaptureQueueCapacity));
        services.AddSingleton(provider =>
            new FinalizationService(
                provider.GetRequiredService<McpSessionWorkspace>(),
                provider.GetRequiredService<IArtifactResourceStore>()));
        services.AddSingleton(_ =>
            new ViewerChildLauncher(
                new ViewerChildLauncherOptions(
                    options.ViewerExecutableRoot,
                    options.ViewerExecutablePath)));
        services.AddSingleton<IOpenUsdMcpService, OpenUsdMcpService>();
        return services;
    }

    private static string GetPath(string variableName, string defaultValue)
    {
        string value = Environment.GetEnvironmentVariable(variableName) ?? defaultValue;
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Path.GetFullPath(value);
    }

    private static int GetNonNegativeInt(string variableName, int defaultValue)
    {
        int value = GetInt(variableName, defaultValue);
        ArgumentOutOfRangeException.ThrowIfNegative(value, variableName);
        return value;
    }

    private static int GetPositiveInt(string variableName, int defaultValue)
    {
        int value = GetInt(variableName, defaultValue);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, variableName);
        return value;
    }

    private static int GetInt(string variableName, int defaultValue)
    {
        string? configured = Environment.GetEnvironmentVariable(variableName);
        if (configured is null)
        {
            return defaultValue;
        }

        if (!int.TryParse(
                configured,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int value))
        {
            throw new ArgumentException(
                $"Environment variable {variableName} must be a base-10 integer.",
                variableName);
        }

        return value;
    }

    private static long GetPositiveLong(string variableName, long defaultValue)
    {
        string? configured = Environment.GetEnvironmentVariable(variableName);
        if (configured is null)
        {
            return defaultValue;
        }

        if (!long.TryParse(
                configured,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out long value) ||
            value <= 0)
        {
            throw new ArgumentException(
                $"Environment variable {variableName} must be a positive base-10 integer.",
                variableName);
        }

        return value;
    }
}
