// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace OpenUsd.Viewer;

internal sealed class ViewerWindowTheme : IDisposable
{
    private readonly Window _window;
    private readonly ViewerThemeContrast _contrast;
    private IPlatformSettings? _platformSettings;
    private IDisposable? _ownerThemeBinding;
    private bool _disposed;

    private ViewerWindowTheme(Window window)
    {
        _window = window;
        _contrast = new ViewerThemeContrast(window);
        window.Opened += OnOpened;
        window.Closed += OnClosed;
    }

    internal static ViewerWindowTheme Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return new ViewerWindowTheme(window);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _window.Opened -= OnOpened;
        if (_window.Owner is { } owner)
        {
            _ownerThemeBinding = _window.Bind(
                TopLevel.RequestedThemeVariantProperty,
                owner.GetObservable(TopLevel.RequestedThemeVariantProperty));
        }
        _platformSettings = Application.Current?.TryGetFeature(typeof(IPlatformSettings))
            as IPlatformSettings;
        if (_platformSettings is { } settings)
        {
            _contrast.Apply(settings.GetColorValues().ContrastPreference);
            settings.ColorValuesChanged += OnColorValuesChanged;
        }
    }

    private void OnColorValuesChanged(object? sender, PlatformColorValues values)
    {
        _window.Dispatcher.Post(() =>
        {
            if (!_disposed)
            {
                _contrast.Apply(values.ContrastPreference);
            }
        });
    }

    private void OnClosed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _window.Opened -= OnOpened;
        _window.Closed -= OnClosed;
        if (_platformSettings is { } settings)
        {
            settings.ColorValuesChanged -= OnColorValuesChanged;
        }
        _ownerThemeBinding?.Dispose();
        _ownerThemeBinding = null;
        _platformSettings = null;
    }
}
