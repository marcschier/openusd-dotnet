// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text;

namespace OpenUsd.Viewer;

internal enum ViewerSettingsLoadStatus
{
    Missing,
    Loaded,
    Migrated,
    Malformed
}

internal sealed record ViewerSettingsLoadResult(
    ViewerSettings Settings,
    ViewerSettingsLoadStatus Status,
    string? Diagnostic);

internal sealed record ViewerSettings
{
    internal const double MinimumWindowWidth = 960;
    internal const double MaximumWindowWidth = 7680;
    internal const double MinimumWindowHeight = 600;
    internal const double MaximumWindowHeight = 4320;
    internal const double MinimumPanelWidth = 180;
    internal const double MaximumPanelWidth = 1600;

    internal static ViewerSettings Default { get; } = new();

    internal double WindowWidth { get; init; } = 1440;

    internal double WindowHeight { get; init; } = 900;

    internal double StagePanelWidth { get; init; } = 280;

    internal double InspectorPanelWidth { get; init; } = 340;

    internal string RendererPreference { get; init; } = "Auto";

    internal int SelectedInspectorTab { get; init; }

    internal bool StagePanelVisible { get; init; } = true;

    internal bool InspectorPanelVisible { get; init; } = true;

    internal bool TimelineVisible { get; init; } = true;

    internal bool DiagnosticsVisible { get; init; } = true;

    internal bool SnapTimelineToFrames { get; init; }

    internal bool IsValid() =>
        IsInRange(WindowWidth, MinimumWindowWidth, MaximumWindowWidth) &&
        IsInRange(WindowHeight, MinimumWindowHeight, MaximumWindowHeight) &&
        IsInRange(StagePanelWidth, MinimumPanelWidth, MaximumPanelWidth) &&
        IsInRange(InspectorPanelWidth, MinimumPanelWidth, MaximumPanelWidth) &&
        SelectedInspectorTab is >= 0 and <= 3 &&
        IsRendererPreference(RendererPreference);

    internal static bool IsRendererPreference(string value) =>
        value is "Auto" or "Storm" or "D3D12" or "Vulkan" or "Metal";

    private static bool IsInRange(double value, double minimum, double maximum) =>
        double.IsFinite(value) && value >= minimum && value <= maximum;
}

internal sealed class ViewerSettingsStore : IDisposable
{
    internal const int MaximumFileBytes = 16 * 1024;
    private const int CurrentVersion = 1;
    private const string FileName = "viewer-settings.txt";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    internal ViewerSettingsStore(string? rootPath = null)
    {
        RootPath = rootPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenUsd",
            "Viewer");
        StorePath = Path.Combine(RootPath, FileName);
    }

    internal string RootPath { get; }

    internal string StorePath { get; }

    internal async Task<ViewerSettingsLoadResult> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(StorePath))
            {
                return new ViewerSettingsLoadResult(
                    ViewerSettings.Default,
                    ViewerSettingsLoadStatus.Missing,
                    null);
            }

            var file = new FileInfo(StorePath);
            if (file.Length > MaximumFileBytes)
            {
                return Malformed(
                    $"The settings file exceeds the {MaximumFileBytes}-byte safety limit.");
            }

            string content = await File.ReadAllTextAsync(
                StorePath,
                Encoding.UTF8,
                cancellationToken).ConfigureAwait(false);
            if (Encoding.UTF8.GetByteCount(content) > MaximumFileBytes)
            {
                return Malformed(
                    $"The settings file exceeds the {MaximumFileBytes}-byte safety limit.");
            }
            return Parse(content);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task SaveAsync(
        ViewerSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!settings.IsValid())
        {
            throw new ArgumentException(
                "Viewer settings contain a value outside the supported range.",
                nameof(settings));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(RootPath);
            string content = Serialize(settings);
            string temporaryPath = Path.Combine(
                RootPath,
                string.Concat(FileName, ".", Guid.NewGuid().ToString("N"), ".tmp"));
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                await using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                File.Move(temporaryPath, StorePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static ViewerSettingsLoadResult Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        string[] lines = content.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return Malformed("The settings file is empty.");
        }

        int version;
        if (string.Equals(lines[0], "openusd-viewer-settings=1", StringComparison.Ordinal))
        {
            version = CurrentVersion;
        }
        else if (string.Equals(lines[0], "version=0", StringComparison.Ordinal))
        {
            version = 0;
        }
        else
        {
            return Malformed("The settings version header is missing or unsupported.");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 1; index < lines.Length; index++)
        {
            string line = lines[index];
            int separator = line.IndexOf('=');
            if (separator <= 0 || separator == line.Length - 1)
            {
                return Malformed($"Settings line {index + 1} is malformed.");
            }
            string key = line[..separator];
            string value = line[(separator + 1)..];
            if (!values.TryAdd(key, value))
            {
                return Malformed($"Settings key '{key}' is duplicated.");
            }
        }

        ViewerSettings settings;
        try
        {
            settings = version == 0
                ? ParseLegacy(values)
                : ParseCurrent(values);
        }
        catch (FormatException exception)
        {
            return Malformed(exception.Message);
        }
        if (!settings.IsValid())
        {
            return Malformed("One or more settings values are outside the supported range.");
        }
        return new ViewerSettingsLoadResult(
            settings,
            version == CurrentVersion
                ? ViewerSettingsLoadStatus.Loaded
                : ViewerSettingsLoadStatus.Migrated,
            version == CurrentVersion
                ? null
                : "Legacy Viewer settings were migrated in memory and will be upgraded on save.");
    }

    private static ViewerSettings ParseCurrent(IReadOnlyDictionary<string, string> values) =>
        new()
        {
            WindowWidth = GetDouble(values, "windowWidth", ViewerSettings.Default.WindowWidth),
            WindowHeight = GetDouble(values, "windowHeight", ViewerSettings.Default.WindowHeight),
            StagePanelWidth = GetDouble(
                values,
                "stagePanelWidth",
                ViewerSettings.Default.StagePanelWidth),
            InspectorPanelWidth = GetDouble(
                values,
                "inspectorPanelWidth",
                ViewerSettings.Default.InspectorPanelWidth),
            RendererPreference = GetString(
                values,
                "renderer",
                ViewerSettings.Default.RendererPreference),
            SelectedInspectorTab = GetInt32(
                values,
                "selectedTab",
                ViewerSettings.Default.SelectedInspectorTab),
            StagePanelVisible = GetBoolean(
                values,
                "stagePanelVisible",
                ViewerSettings.Default.StagePanelVisible),
            InspectorPanelVisible = GetBoolean(
                values,
                "inspectorPanelVisible",
                ViewerSettings.Default.InspectorPanelVisible),
            TimelineVisible = GetBoolean(
                values,
                "timelineVisible",
                ViewerSettings.Default.TimelineVisible),
            DiagnosticsVisible = GetBoolean(
                values,
                "diagnosticsVisible",
                ViewerSettings.Default.DiagnosticsVisible),
            SnapTimelineToFrames = GetBoolean(
                values,
                "snapTimelineToFrames",
                ViewerSettings.Default.SnapTimelineToFrames)
        };

    private static ViewerSettings ParseLegacy(IReadOnlyDictionary<string, string> values) =>
        ViewerSettings.Default with
        {
            WindowWidth = GetDouble(values, "width", ViewerSettings.Default.WindowWidth),
            WindowHeight = GetDouble(values, "height", ViewerSettings.Default.WindowHeight),
            RendererPreference = GetString(
                values,
                "renderer",
                ViewerSettings.Default.RendererPreference)
        };

    private static string Serialize(ViewerSettings settings)
    {
        var builder = new StringBuilder(384);
        builder.AppendLine("openusd-viewer-settings=1");
        Append(builder, "windowWidth", settings.WindowWidth);
        Append(builder, "windowHeight", settings.WindowHeight);
        Append(builder, "stagePanelWidth", settings.StagePanelWidth);
        Append(builder, "inspectorPanelWidth", settings.InspectorPanelWidth);
        builder.Append("renderer=").AppendLine(settings.RendererPreference);
        builder.Append("selectedTab=")
            .AppendLine(settings.SelectedInspectorTab.ToString(CultureInfo.InvariantCulture));
        Append(builder, "stagePanelVisible", settings.StagePanelVisible);
        Append(builder, "inspectorPanelVisible", settings.InspectorPanelVisible);
        Append(builder, "timelineVisible", settings.TimelineVisible);
        Append(builder, "diagnosticsVisible", settings.DiagnosticsVisible);
        Append(builder, "snapTimelineToFrames", settings.SnapTimelineToFrames);
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string key, double value) =>
        builder.Append(key)
            .Append('=')
            .AppendLine(value.ToString("G17", CultureInfo.InvariantCulture));

    private static void Append(StringBuilder builder, string key, bool value) =>
        builder.Append(key).Append('=').AppendLine(value ? "true" : "false");

    private static double GetDouble(
        IReadOnlyDictionary<string, string> values,
        string key,
        double fallback) =>
        !values.TryGetValue(key, out string? value)
            ? fallback
            : double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsed)
                ? parsed
                : double.NaN;

    private static int GetInt32(
        IReadOnlyDictionary<string, string> values,
        string key,
        int fallback) =>
        !values.TryGetValue(key, out string? value)
            ? fallback
            : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : int.MinValue;

    private static bool GetBoolean(
        IReadOnlyDictionary<string, string> values,
        string key,
        bool fallback)
    {
        if (!values.TryGetValue(key, out string? value))
        {
            return fallback;
        }
        return value switch
        {
            "true" => true,
            "false" => false,
            _ => throw new FormatException($"Settings key '{key}' is not a Boolean.")
        };
    }

    private static string GetString(
        IReadOnlyDictionary<string, string> values,
        string key,
        string fallback) =>
        values.TryGetValue(key, out string? value) ? value : fallback;

    private static ViewerSettingsLoadResult Malformed(string diagnostic) =>
        new(ViewerSettings.Default, ViewerSettingsLoadStatus.Malformed, diagnostic);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _gate.Dispose();
    }
}
