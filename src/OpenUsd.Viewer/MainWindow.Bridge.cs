// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Threading;

namespace OpenUsd.Viewer;

/// <summary>
/// Connects the shell's bridge surface - the <c>Tools &gt; Connections &gt; Omniverse
/// Bridge</c> entry, its dialog, and the status-bar indicator - to the optional
/// host-injected provider.
/// </summary>
/// <remarks>
/// <para>
/// This file is the only adapter between live controls and
/// <see cref="ViewerBridgeConnectionModel"/>. It contains no connection logic: visibility,
/// enablement, wording, and command gating are all decided by the model, which is why they
/// can be tested without a window. What lives here is the part that genuinely needs Avalonia:
/// posting the model's callbacks onto the dispatcher and assigning the results to controls.
/// </para>
/// <para>
/// When no host injected a provider the model reports the absent state, the menu entry stays
/// hidden and disabled exactly as it ships in markup, and nothing in this file ever runs
/// again. Opening, rendering, and simulating a local stage is untouched.
/// </para>
/// </remarks>
public sealed partial class MainWindow
{
    private ViewerBridgeConnectionModel? _bridgeConnection;
    private BridgeConnectionWindow? _bridgeDialog;

    /// <summary>
    /// Builds the bridge model for whatever the host supplied and applies its first state.
    /// </summary>
    private void WireBridgeConnection()
    {
        _bridgeConnection = new ViewerBridgeConnectionModel(
            ViewerStartupOptions.BridgeConnection,
            PostBridgeCallback,
            ApplyBridgeState);
        ToolsOmniverseBridgeMenuItem.Click += OnOmniverseBridgeClick;
        _bridgeConnection.Refresh();
    }

    /// <summary>
    /// Marshals a model callback onto the UI thread. The model calls this from a provider's
    /// own transport thread, so the post must never wait: a provider that is slow, or a UI
    /// thread that is busy rendering, must not be able to stall the other.
    /// </summary>
    private static void PostBridgeCallback(Action callback)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            callback();
            return;
        }
        Dispatcher.UIThread.Post(callback, DispatcherPriority.Background);
    }

    private void ApplyBridgeState(ViewerBridgeViewState state)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        ToolsOmniverseBridgeMenuItem.IsVisible = state.MenuVisible;
        ToolsOmniverseBridgeMenuItem.IsEnabled = state.MenuEnabled;
        ToolTip.SetTip(ToolsOmniverseBridgeMenuItem, state.MenuToolTip);
        BridgeStatus.IsVisible = state.StatusVisible;
        BridgeStatus.Text = state.StatusText;
        _bridgeDialog?.Apply(state);
    }

    private void OnOmniverseBridgeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_bridgeConnection is not { HasProvider: true } model)
        {
            return;
        }

        if (_bridgeDialog is { } existing)
        {
            existing.Activate();
            return;
        }

        var dialog = new BridgeConnectionWindow(model);
        dialog.SetProviderName(model.ProviderDisplayName);
        _bridgeDialog = dialog;
        dialog.Closed += (_, _) => _bridgeDialog = null;
        dialog.Show(this);
    }

    /// <summary>
    /// Tears the bridge surface down with the window: the dialog is closed, the provider
    /// subscription is dropped, and any in-flight command is cancelled, so a host provider
    /// cannot keep a closed window's controls alive through its own event.
    /// </summary>
    private void DisposeBridgeConnection()
    {
        _bridgeDialog?.Close();
        _bridgeDialog = null;
        _bridgeConnection?.Dispose();
        _bridgeConnection = null;
    }
}
