// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Mcp;

public sealed record ViewerChildLauncherOptions(
    string ExecutableRoot,
    string ExecutablePath,
    SilkPreparationLimits? PreparationLimits = null,
    SilkGpuBufferBudget? GpuBufferBudget = null)
{
    /// <summary>Gets effective limits forwarded as independent child-process pools, not shared parent credit.</summary>
    public SilkTexturePreparationBudgets? TexturePreparationBudgets { get; init; }
    /// <summary>Gets the explicit material streaming choice forwarded to the child.</summary>
    public bool StreamMaterialTextures { get; init; }
    /// <summary>Gets the explicit D3D12 GPU-upload policy forwarded to the child.</summary>
    public bool D3D12GpuUploadHeaps { get; init; }
}

public sealed record ViewerLaunchRequest(
    string StagePath,
    string PluginPath,
    string Renderer,
    string? CameraPath = null);

public sealed record ViewerProcessMetadata(
    int ProcessId,
    DateTimeOffset StartedAt,
    string ExecutablePath,
    IReadOnlyList<string> Arguments);

public sealed class ViewerChildLauncher
{
    private readonly IViewerProcessStarter _processStarter;
    private readonly string _executablePath;
    private readonly SilkPreparationLimits? _preparationLimits;
    private readonly SilkGpuBufferBudget? _gpuBufferBudget;
    private readonly SilkTexturePreparationBudgets? _textureBudgets;
    private readonly bool _streamMaterialTextures;
    private readonly bool _gpuUploadHeaps;

    public ViewerChildLauncher(ViewerChildLauncherOptions options)
        : this(options, new ViewerProcessStarter())
    {
    }

    internal ViewerChildLauncher(
        ViewerChildLauncherOptions options,
        IViewerProcessStarter processStarter)
    {
        ArgumentNullException.ThrowIfNull(options);
        _processStarter = processStarter
            ?? throw new ArgumentNullException(nameof(processStarter));
        _executablePath = ValidateExecutable(options);
        _preparationLimits = options.PreparationLimits;
        _gpuBufferBudget = options.GpuBufferBudget;
        _textureBudgets = options.TexturePreparationBudgets;
        _streamMaterialTextures = options.StreamMaterialTextures;
        _gpuUploadHeaps = options.D3D12GpuUploadHeaps;
    }

    public ViewerProcessMetadata Launch(ViewerLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ProcessStartInfo startInfo = CreateStartInfo(_executablePath, request, _preparationLimits, _gpuBufferBudget,
            _textureBudgets, _streamMaterialTextures, _gpuUploadHeaps);
        return _processStarter.Start(startInfo);
    }

    internal static ProcessStartInfo CreateStartInfo(
        string executablePath,
        ViewerLaunchRequest request,
        SilkPreparationLimits? preparationLimits = null,
        SilkGpuBufferBudget? gpuBufferBudget = null,
        SilkTexturePreparationBudgets? texturePreparationBudgets = null,
        bool streamMaterialTextures = false,
        bool gpuUploadHeaps = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(request);
        string stagePath = ValidateExistingFile(request.StagePath, nameof(request.StagePath));
        string pluginPath = ValidateExistingDirectory(
            request.PluginPath,
            nameof(request.PluginPath));
        ValidateArgumentValue(request.Renderer, nameof(request.Renderer));
        if (gpuUploadHeaps &&
            (gpuBufferBudget is null ||
             (!string.Equals(request.Renderer, "D3D12", StringComparison.OrdinalIgnoreCase) &&
              !string.Equals(request.Renderer, "Auto", StringComparison.OrdinalIgnoreCase))))
        {
            throw new ArgumentException("GPU-upload heaps require a D3D12/Auto child and an explicit GPU buffer budget.");
        }
        if (streamMaterialTextures && gpuBufferBudget is null)
        {
            throw new ArgumentException("Material streaming requires a configured GPU buffer budget.",
                nameof(gpuBufferBudget));
        }
        if ((preparationLimits is not null || gpuBufferBudget is not null || texturePreparationBudgets is not null) &&
            string.Equals(request.Renderer, "Storm", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Storm cannot enforce the configured preparation or resource ceilings.");
        }
        if (request.CameraPath is not null)
        {
            ValidateArgumentValue(request.CameraPath, nameof(request.CameraPath));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false,
        };
        startInfo.Environment.Remove("OPENUSD_SILK_MESH_RESERVATION_BYTES");
        startInfo.Environment.Remove("OPENUSD_SILK_MAX_PAGE_BYTES");
        startInfo.Environment.Remove("OPENUSD_SILK_STORAGE_LIVE_BYTES");
        startInfo.Environment.Remove("OPENUSD_SILK_STORAGE_UPDATE_BYTES");
        startInfo.Environment.Remove("OPENUSD_SILK_STORAGE_COMMAND_BYTES");
        startInfo.Environment.Remove("OPENUSD_SILK_STORAGE_TRANSFER_BYTES");
        startInfo.Environment.Remove("OPENUSD_SILK_GPU_BUFFER_BYTES");
        startInfo.Environment.Remove("OPENUSD_SILK_CPU_TEXTURE_BYTES");
        startInfo.Environment.Remove("OPENUSD_SILK_GPU_TEXTURE_BYTES");
        startInfo.Environment.Remove("OPENUSD_SILK_GPU_STAGING_BYTES");
        startInfo.Environment.Remove("OPENUSD_SILK_STREAM_MATERIAL_TEXTURES");
        startInfo.Environment.Remove("OPENUSD_D3D12_GPU_UPLOAD_HEAPS");
        if (gpuUploadHeaps)
        {
            startInfo.Environment["OPENUSD_D3D12_GPU_UPLOAD_HEAPS"] = "true";
        }
        if (streamMaterialTextures)
        {
            startInfo.Environment["OPENUSD_SILK_STREAM_MATERIAL_TEXTURES"] = "true";
        }
        if (preparationLimits is not null)
        {
            startInfo.Environment["OPENUSD_SILK_MESH_RESERVATION_BYTES"] =
                preparationLimits.MaximumMeshPreparationReservationBytes.ToString(CultureInfo.InvariantCulture);
            startInfo.Environment["OPENUSD_SILK_MAX_PAGE_BYTES"] =
                preparationLimits.MaximumCommandPageBytes.ToString(CultureInfo.InvariantCulture);
        }
        if (gpuBufferBudget is not null)
        {
            startInfo.Environment["OPENUSD_SILK_GPU_BUFFER_BYTES"] =
                gpuBufferBudget.MaximumBytes.ToString(CultureInfo.InvariantCulture);
        }
        if (preparationLimits?.CommandStorage is { } storage)
        {
            startInfo.Environment["OPENUSD_SILK_STORAGE_LIVE_BYTES"] =
                storage.MaximumLiveBytes.ToString(CultureInfo.InvariantCulture);
            startInfo.Environment["OPENUSD_SILK_STORAGE_UPDATE_BYTES"] =
                storage.MaximumUpdateBytes.ToString(CultureInfo.InvariantCulture);
            startInfo.Environment["OPENUSD_SILK_STORAGE_COMMAND_BYTES"] =
                storage.MaximumCommandBytes.ToString(CultureInfo.InvariantCulture);
            startInfo.Environment["OPENUSD_SILK_STORAGE_TRANSFER_BYTES"] =
                storage.TransferBufferBytes.ToString(CultureInfo.InvariantCulture);
        }
        if (texturePreparationBudgets?.CpuTextureBudget is { } cpu)
        {
            startInfo.Environment["OPENUSD_SILK_CPU_TEXTURE_BYTES"] =
                cpu.MaximumBytes.ToString(CultureInfo.InvariantCulture);
        }
        if (texturePreparationBudgets?.GpuTextureBudget is { } textures)
        {
            startInfo.Environment["OPENUSD_SILK_GPU_TEXTURE_BYTES"] =
                textures.MaximumBytes.ToString(CultureInfo.InvariantCulture);
        }
        if (texturePreparationBudgets?.GpuStagingBudget is { } staging)
        {
            startInfo.Environment["OPENUSD_SILK_GPU_STAGING_BYTES"] =
                staging.MaximumBytes.ToString(CultureInfo.InvariantCulture);
        }
        startInfo.ArgumentList.Add("--stage");
        startInfo.ArgumentList.Add(stagePath);
        startInfo.ArgumentList.Add("--plugins");
        startInfo.ArgumentList.Add(pluginPath);
        startInfo.ArgumentList.Add("--renderer");
        startInfo.ArgumentList.Add(request.Renderer);
        if (request.CameraPath is not null)
        {
            startInfo.ArgumentList.Add("--camera");
            startInfo.ArgumentList.Add(request.CameraPath);
        }

        return startInfo;
    }

    private static string ValidateExecutable(ViewerChildLauncherOptions options)
    {
        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(options.ExecutableRoot));
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"The configured Viewer executable root does not exist: '{root}'.");
        }

        string executablePath = Path.GetFullPath(options.ExecutablePath);
        string prefix = string.Concat(root, Path.DirectorySeparatorChar);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!executablePath.StartsWith(prefix, comparison))
        {
            throw new ArgumentException(
                "The configured Viewer executable escapes its allowed root.",
                nameof(options));
        }

        string expectedName = OperatingSystem.IsWindows()
            ? "OpenUsd.Viewer.App.exe"
            : "OpenUsd.Viewer.App";
        if (!string.Equals(Path.GetFileName(executablePath), expectedName, comparison))
        {
            throw new ArgumentException(
                $"The configured executable must be named '{expectedName}'.",
                nameof(options));
        }

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "The configured Viewer executable does not exist.",
                executablePath);
        }

        RejectReparsePoints(root, executablePath);
        return executablePath;
    }

    private static string ValidateExistingDirectory(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        ValidateArgumentValue(path, parameterName);
        string fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(
                $"The configured directory does not exist: '{fullPath}'.");
        }

        return fullPath;
    }

    private static string ValidateExistingFile(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        ValidateArgumentValue(path, parameterName);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "The stage supplied to Viewer does not exist.",
                fullPath);
        }

        return fullPath;
    }

    private static void ValidateArgumentValue(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Viewer argument values cannot contain control characters.",
                parameterName);
        }
    }

    private static void RejectReparsePoints(string root, string executablePath)
    {
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                $"Reparse points are not allowed in the Viewer executable path: '{root}'.");
        }

        string current = root;
        foreach (string segment in Path.GetRelativePath(root, executablePath).Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    $"Reparse points are not allowed in the Viewer executable path: '{current}'.");
            }
        }
    }
}

internal interface IViewerProcessStarter
{
    ViewerProcessMetadata Start(ProcessStartInfo startInfo);
}

internal sealed class ViewerProcessStarter : IViewerProcessStarter
{
    public ViewerProcessMetadata Start(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The Viewer process could not be started.");
        process.StandardInput.Close();
        Task standardOutput = process.StandardOutput.ReadToEndAsync();
        Task standardError = process.StandardError.ReadToEndAsync();
        _ = DisposeAfterExitAsync(process, standardOutput, standardError);
        return new ViewerProcessMetadata(
            process.Id,
            DateTimeOffset.UtcNow,
            startInfo.FileName,
            Array.AsReadOnly(startInfo.ArgumentList.ToArray()));
    }

    private static async Task DisposeAfterExitAsync(
        Process process,
        Task standardOutput,
        Task standardError)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false);
        }
        finally
        {
            process.Dispose();
        }
    }
}
