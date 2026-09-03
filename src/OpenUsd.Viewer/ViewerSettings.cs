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

    /// <summary>
    /// The stable, string <see cref="ViewerInspectorLayoutPolicy"/> tab identity that was
    /// selected, never a visual index. <see cref="ViewerInspectorLayoutPolicy.ResolveSelectedTabId"/>
    /// is the only place that decides what happens when this tab is hidden or unknown.
    /// </summary>
    internal string SelectedTabId { get; init; } = ViewerInspectorLayoutPolicy.PropertiesTabId;

    internal bool StagePanelVisible { get; init; } = true;

    internal bool InspectorPanelVisible { get; init; } = true;

    internal bool TimelineVisible { get; init; } = true;

    /// <summary>
    /// Whether the Diagnostics developer tab is visible. Clean (v2) profiles default this to
    /// <see langword="false"/>; migrated v1 profiles preserve whatever was persisted, because
    /// that flag already existed and already governed this tab in v1.
    /// </summary>
    internal bool DiagnosticsVisible { get; init; }

    /// <summary>
    /// Whether the Hydra scene developer tab is visible. Clean (v2) profiles default this to
    /// <see langword="false"/>; migrated v1 profiles force this to <see langword="true"/>,
    /// because v1 always showed this tab unconditionally and had no flag to preserve.
    /// </summary>
    internal bool HydraVisible { get; init; }

    /// <summary>
    /// Whether the TfDebug developer tab is visible. Clean (v2) profiles default this to
    /// <see langword="false"/>; migrated v1 profiles force this to <see langword="true"/>,
    /// because v1 always showed this tab unconditionally and had no flag to preserve.
    /// </summary>
    internal bool TfDebugVisible { get; init; }

    internal bool SnapTimelineToFrames { get; init; }

    /// <summary>
    /// The persisted pick target every viewport click resolves, as the stable
    /// token <see cref="ViewerPickTargetPolicy"/> defines. Clean (v3) profiles
    /// default to the prim target; a migrated v0, v1, or v2 profile gets the
    /// same default, because no earlier format had the concept and inventing one
    /// would silently change what a click means for a returning user.
    /// </summary>
    internal string PickTarget { get; init; } =
        ViewerPickTargetPolicy.ToToken(ViewerPickTargetPolicy.DefaultTarget);

    /// <summary>
    /// The persisted selection-outline mode. Clean (v3) profiles and every
    /// migrated profile default to the depth-tested visible-only outline, which
    /// is exactly what every earlier version rendered.
    /// </summary>
    internal string SelectionMode { get; init; } =
        ViewerPickTargetPolicy.ToToken(ViewerPickTargetPolicy.DefaultSelectionMode);

    /// <summary>
    /// The persisted colour-management choice. Only a config path and colour-space,
    /// display, view, and look names are ever stored; nothing here is a secret.
    /// </summary>
    internal ViewerColorManagement ColorManagement { get; init; } =
        ViewerColorManagement.Default;

    internal bool IsValid() =>
        IsInRange(WindowWidth, MinimumWindowWidth, MaximumWindowWidth) &&
        IsInRange(WindowHeight, MinimumWindowHeight, MaximumWindowHeight) &&
        IsInRange(StagePanelWidth, MinimumPanelWidth, MaximumPanelWidth) &&
        IsInRange(InspectorPanelWidth, MinimumPanelWidth, MaximumPanelWidth) &&
        ViewerInspectorLayoutPolicy.IsKnownTab(SelectedTabId) &&
        ColorManagement.IsValid() &&
        IsPickTargetToken(PickTarget) &&
        IsSelectionModeToken(SelectionMode) &&
        IsRendererPreference(RendererPreference);

    internal static bool IsPickTargetToken(string value) =>
        value is "primitive" or "face" or "edge" or "point";

    internal static bool IsSelectionModeToken(string value) =>
        value is "visibleOnly" or "xray";

    internal static bool IsRendererPreference(string value) =>
        value is "Auto" or "Storm" or "D3D12" or "Vulkan" or "Metal";

    private static bool IsInRange(double value, double minimum, double maximum) =>
        double.IsFinite(value) && value >= minimum && value <= maximum;
}

internal sealed class ViewerSettingsStore : IDisposable
{
    internal const int MaximumFileBytes = 16 * 1024;
    private const int CurrentVersion = 3;
    private const string FileName = "viewer-settings.txt";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// The v1 <c>selectedTab</c> index-to-tab-identity mapping, preserved exactly so a
    /// migrated v1 profile keeps whatever tab it had selected. Index 10 (the removed
    /// Settings tab) was never actually persisted: the v1 writer clamped every persisted
    /// index to 9, so a literal persisted <c>9</c> cannot be distinguished from a Settings
    /// selection and is preserved as TfDebug (index 9) rather than guessed at. This is the
    /// one documented, unrecoverable legacy ambiguity from the v1 format.
    /// </summary>
    private static readonly string[] V1TabIdByIndex =
    [
        ViewerInspectorLayoutPolicy.PropertiesTabId,
        ViewerInspectorLayoutPolicy.ValueTabId,
        ViewerInspectorLayoutPolicy.MetadataTabId,
        ViewerInspectorLayoutPolicy.CompositionTabId,
        ViewerInspectorLayoutPolicy.LayersTabId,
        ViewerInspectorLayoutPolicy.DiagnosticsTabId,
        ViewerInspectorLayoutPolicy.ValidationTabId,
        ViewerInspectorLayoutPolicy.HydraTabId,
        ViewerInspectorLayoutPolicy.PhysicsTabId,
        ViewerInspectorLayoutPolicy.TfDebugTabId,
    ];

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
        if (string.Equals(lines[0], "openusd-viewer-settings=3", StringComparison.Ordinal))
        {
            version = 3;
        }
        else if (string.Equals(lines[0], "openusd-viewer-settings=2", StringComparison.Ordinal))
        {
            version = 2;
        }
        else if (string.Equals(lines[0], "openusd-viewer-settings=1", StringComparison.Ordinal))
        {
            version = 1;
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
            settings = version switch
            {
                0 => ParseLegacy(values),
                1 => ParseV1(values),
                // A v2 profile predates the pick-target and selection-mode keys
                // entirely, so it reads through the same parser and simply finds
                // neither key. That is the whole migration: both settings fall
                // back to the defaults that reproduce v2 behaviour exactly, and
                // the profile is rewritten with them on the next save.
                _ => ParseCurrent(values),
            };
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
            SelectedTabId = GetString(
                values,
                "selectedTabId",
                ViewerSettings.Default.SelectedTabId),
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
            HydraVisible = GetBoolean(
                values,
                "hydraVisible",
                ViewerSettings.Default.HydraVisible),
            TfDebugVisible = GetBoolean(
                values,
                "tfDebugVisible",
                ViewerSettings.Default.TfDebugVisible),
            SnapTimelineToFrames = GetBoolean(
                values,
                "snapTimelineToFrames",
                ViewerSettings.Default.SnapTimelineToFrames),
            PickTarget = ViewerPickTargetPolicy.ToToken(
                ViewerPickTargetPolicy.FromToken(
                    values.GetValueOrDefault("pickTarget"))),
            SelectionMode = ViewerPickTargetPolicy.ToToken(
                ViewerPickTargetPolicy.SelectionModeFromToken(
                    values.GetValueOrDefault("selectionMode"))),
            ColorManagement = ParseColorManagement(values)
        };

    private static ViewerColorManagement ParseColorManagement(
        IReadOnlyDictionary<string, string> values) =>
        new()
        {
            Enabled = GetBoolean(
                values,
                "colorManagementEnabled",
                ViewerColorManagement.Default.Enabled),
            ConfigPath = GetString(
                values,
                "colorManagementConfigPath",
                ViewerColorManagement.Default.ConfigPath),
            SourceColorSpace = GetString(
                values,
                "colorManagementSourceColorSpace",
                ViewerColorManagement.Default.SourceColorSpace),
            Display = GetString(
                values,
                "colorManagementDisplay",
                ViewerColorManagement.Default.Display),
            View = GetString(
                values,
                "colorManagementView",
                ViewerColorManagement.Default.View),
            Look = GetString(
                values,
                "colorManagementLook",
                ViewerColorManagement.Default.Look)
        };

    /// <summary>
    /// Parses the v1 format (header <c>openusd-viewer-settings=1</c>), preserving the user's
    /// prior layout choices instead of resetting them to the v2 clean defaults.
    /// </summary>
    /// <remarks>
    /// The v1 <c>selectedTab</c> integer is translated through <see cref="V1TabIdByIndex"/>.
    /// <c>diagnosticsVisible</c> is preserved as authored, because v1 already had that flag
    /// and it already governed the Diagnostics tab. Hydra and TfDebug had no visibility flag
    /// in v1 and were always shown, so both are initialized visible here rather than
    /// defaulting to the v2 clean default of hidden: preserving an existing user's layout
    /// takes priority over a clean default that only applies to new profiles.
    /// </remarks>
    private static ViewerSettings ParseV1(IReadOnlyDictionary<string, string> values)
    {
        int selectedIndex = GetInt32(values, "selectedTab", 0);
        string selectedTabId = selectedIndex >= 0 && selectedIndex < V1TabIdByIndex.Length
            ? V1TabIdByIndex[selectedIndex]
            : throw new FormatException(
                $"Settings key 'selectedTab' value '{selectedIndex}' is out of the v1 range.");
        return new ViewerSettings
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
            SelectedTabId = selectedTabId,
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
            DiagnosticsVisible = GetBoolean(values, "diagnosticsVisible", fallback: true),
            HydraVisible = true,
            TfDebugVisible = true,
            SnapTimelineToFrames = GetBoolean(
                values,
                "snapTimelineToFrames",
                ViewerSettings.Default.SnapTimelineToFrames)
        };
    }

    /// <summary>
    /// Parses the v0 format (header <c>version=0</c>), which predates every settings field
    /// except window size and renderer preference. Diagnostics, Hydra, and TfDebug visibility
    /// preserve the legacy always-visible behavior rather than the v2 clean default of hidden,
    /// for the same reason v1 does: an existing user's prior layout takes priority over a
    /// default that is only meant to apply to genuinely new profiles. Every other field falls
    /// back to the v2 clean default because v0 never had an opinion about it at all.
    /// </summary>
    private static ViewerSettings ParseLegacy(IReadOnlyDictionary<string, string> values) =>
        ViewerSettings.Default with
        {
            WindowWidth = GetDouble(values, "width", ViewerSettings.Default.WindowWidth),
            WindowHeight = GetDouble(values, "height", ViewerSettings.Default.WindowHeight),
            RendererPreference = GetString(
                values,
                "renderer",
                ViewerSettings.Default.RendererPreference),
            DiagnosticsVisible = true,
            HydraVisible = true,
            TfDebugVisible = true
        };

    private static string Serialize(ViewerSettings settings)
    {
        var builder = new StringBuilder(384);
        builder.AppendLine("openusd-viewer-settings=3");
        Append(builder, "windowWidth", settings.WindowWidth);
        Append(builder, "windowHeight", settings.WindowHeight);
        Append(builder, "stagePanelWidth", settings.StagePanelWidth);
        Append(builder, "inspectorPanelWidth", settings.InspectorPanelWidth);
        builder.Append("renderer=").AppendLine(settings.RendererPreference);
        builder.Append("selectedTabId=").AppendLine(settings.SelectedTabId);
        Append(builder, "stagePanelVisible", settings.StagePanelVisible);
        Append(builder, "inspectorPanelVisible", settings.InspectorPanelVisible);
        Append(builder, "timelineVisible", settings.TimelineVisible);
        Append(builder, "diagnosticsVisible", settings.DiagnosticsVisible);
        Append(builder, "hydraVisible", settings.HydraVisible);
        Append(builder, "tfDebugVisible", settings.TfDebugVisible);
        Append(builder, "snapTimelineToFrames", settings.SnapTimelineToFrames);
        builder.Append("pickTarget=").AppendLine(settings.PickTarget);
        builder.Append("selectionMode=").AppendLine(settings.SelectionMode);
        AppendColorManagement(builder, settings.ColorManagement);
        return builder.ToString();
    }

    /// <summary>
    /// Writes the colour-management choice. Empty values are omitted rather than written
    /// as empty lines, because the line format cannot represent an empty value; a missing
    /// key already means "use the default", which for every one of these is empty.
    /// </summary>
    private static void AppendColorManagement(
        StringBuilder builder,
        ViewerColorManagement colorManagement)
    {
        Append(builder, "colorManagementEnabled", colorManagement.Enabled);
        AppendIfPresent(builder, "colorManagementConfigPath", colorManagement.ConfigPath);
        AppendIfPresent(
            builder,
            "colorManagementSourceColorSpace",
            colorManagement.SourceColorSpace);
        AppendIfPresent(builder, "colorManagementDisplay", colorManagement.Display);
        AppendIfPresent(builder, "colorManagementView", colorManagement.View);
        AppendIfPresent(builder, "colorManagementLook", colorManagement.Look);
    }

    private static void AppendIfPresent(StringBuilder builder, string key, string value)
    {
        if (value.Length == 0)
        {
            return;
        }
        builder.Append(key).Append('=').AppendLine(value);
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
