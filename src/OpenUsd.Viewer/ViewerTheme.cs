// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Styling;

namespace OpenUsd.Viewer;

internal enum ViewerThemePreference
{
    System,
    Light,
    Dark
}

internal sealed class ViewerThemeContrast(Control scope)
{
    private readonly Control _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    private ViewerContrastResources? _resources;

    internal void Apply(ColorContrastPreference preference)
    {
        if (preference == ColorContrastPreference.High)
        {
            _resources ??= new ViewerContrastResources();
            if (!_scope.Resources.MergedDictionaries.Contains(_resources))
            {
                _scope.Resources.MergedDictionaries.Add(_resources);
            }
        }
        else if (preference == ColorContrastPreference.NoPreference)
        {
            if (_resources is not null)
            {
                _scope.Resources.MergedDictionaries.Remove(_resources);
            }
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(preference));
        }
    }
}

internal static class ViewerTheme
{
    internal static ViewerSettings ExecuteCommand(
        Control scope,
        ViewerSettings settings,
        string commandId)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ViewerThemePreference preference = commandId switch
        {
            ViewerCommandIds.ViewThemeSystem => ViewerThemePreference.System,
            ViewerCommandIds.ViewThemeLight => ViewerThemePreference.Light,
            ViewerCommandIds.ViewThemeDark => ViewerThemePreference.Dark,
            _ => throw new ArgumentException("Unknown Viewer theme command.", nameof(commandId))
        };
        Apply(scope, preference);
        return settings with { ThemePreference = preference };
    }

    internal static void Apply(Control scope, ViewerThemePreference preference)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ThemeVariant variant = preference switch
        {
            ViewerThemePreference.System => ThemeVariant.Default,
            ViewerThemePreference.Light => ThemeVariant.Light,
            ViewerThemePreference.Dark => ThemeVariant.Dark,
            _ => throw new ArgumentOutOfRangeException(nameof(preference))
        };
        switch (scope)
        {
            case TopLevel window:
                window.RequestedThemeVariant = variant;
                break;
            case ThemeVariantScope themeScope:
                themeScope.RequestedThemeVariant = variant;
                break;
            default:
                throw new ArgumentException(
                    "Viewer themes require a window or a ThemeVariantScope.", nameof(scope));
        }
    }

    internal static ViewerThemePreference FromToken(string? token) => token switch
    {
        "light" => ViewerThemePreference.Light,
        "dark" => ViewerThemePreference.Dark,
        _ => ViewerThemePreference.System
    };

    internal static string ToToken(ViewerThemePreference preference) => preference switch
    {
        ViewerThemePreference.System => "system",
        ViewerThemePreference.Light => "light",
        ViewerThemePreference.Dark => "dark",
        _ => throw new ArgumentOutOfRangeException(nameof(preference))
    };
}
