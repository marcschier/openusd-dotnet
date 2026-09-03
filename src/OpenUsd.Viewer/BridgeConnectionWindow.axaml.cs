// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;

namespace OpenUsd.Viewer;

/// <summary>
/// The one place an operator drives a host-injected bridge session: pick a session, connect,
/// disconnect, resynchronize, and read a bounded, redacted status.
/// </summary>
/// <remarks>
/// <para>
/// The dialog owns no connection state of its own. It renders whatever
/// <see cref="ViewerBridgeConnectionModel"/> publishes and forwards operator intent back to
/// it, so the status bar, the menu entry, and this dialog can never disagree, and closing the
/// dialog neither drops the session nor cancels an in-flight command.
/// </para>
/// <para>
/// There is deliberately no endpoint field and no credential field. Those are host
/// configuration; a text box here would turn the Viewer into a place credentials are typed
/// and, sooner or later, persisted.
/// </para>
/// </remarks>
internal sealed partial class BridgeConnectionWindow : Window
{
    private readonly ViewerBridgeConnectionModel _model;

    /// <summary>Creates a dialog with no provider. Used by the Avalonia designer.</summary>
    public BridgeConnectionWindow()
        : this(new ViewerBridgeConnectionModel(
            provider: null,
            static action => action(),
            static _ => { }))
    {
    }

    /// <summary>Creates a dialog bound to the shell's bridge model.</summary>
    /// <param name="model">The model that owns the provider and its state.</param>
    internal BridgeConnectionWindow(ViewerBridgeConnectionModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        InitializeComponent();
        AutomationProperties.SetName(
            BridgeConnectButton,
            ViewerCommandCatalog.Get(ViewerCommandIds.ToolsConnectionsBridgeConnect).AccessibleName);
        AutomationProperties.SetName(
            BridgeDisconnectButton,
            ViewerCommandCatalog.Get(
                ViewerCommandIds.ToolsConnectionsBridgeDisconnect).AccessibleName);
        AutomationProperties.SetName(
            BridgeResyncButton,
            ViewerCommandCatalog.Get(ViewerCommandIds.ToolsConnectionsBridgeResync).AccessibleName);
        BridgeConnectButton.Click += async (_, _) =>
            await _model.ConnectAsync(SelectedSessionId()).ConfigureAwait(true);
        BridgeDisconnectButton.Click += async (_, _) =>
            await _model.DisconnectAsync().ConfigureAwait(true);
        BridgeResyncButton.Click += async (_, _) =>
            await _model.ResyncAsync().ConfigureAwait(true);
        BridgeCloseButton.Click += (_, _) => Close();
        Opened += async (_, _) => await LoadSessionsAsync().ConfigureAwait(true);
        Apply(_model.CurrentState);
    }

    /// <summary>Renders a published view state.</summary>
    /// <param name="state">The state the model computed.</param>
    internal void Apply(ViewerBridgeViewState state)
    {
        BridgeStatusText.Text = state.StatusText.Length == 0
            ? "Bridge: disconnected"
            : state.StatusText;
        BridgeCountersText.Text = string.Format(
            CultureInfo.InvariantCulture,
            "Attempts: {0} \u00b7 Applied: {1} \u00b7 Pending: {2} \u00b7 Dropped notices: {3}",
            state.Status.ConnectAttemptCount,
            state.Status.AppliedUpdateCount,
            state.Status.PendingOutboundCount,
            state.DroppedStatusEventCount);
        BridgeErrorText.Text = state.ErrorMessage ?? string.Empty;
        BridgeErrorText.IsVisible = state.ErrorMessage is not null;
        BridgeConnectButton.IsEnabled = state.CanConnect;
        BridgeDisconnectButton.IsEnabled = state.CanDisconnect;
        BridgeResyncButton.IsEnabled = state.CanResync;
        BridgeSessionSelector.IsEnabled = !state.Busy;
    }

    /// <summary>Shows the provider name in the dialog header.</summary>
    /// <param name="displayName">The provider's own display name.</param>
    internal void SetProviderName(string displayName) =>
        BridgeProviderText.Text = ViewerBridgeText.Bound(displayName) ?? "Omniverse Bridge";

    private string? SelectedSessionId() =>
        (BridgeSessionSelector.SelectedItem as ComboBoxItem)?.Tag as string;

    private async Task LoadSessionsAsync()
    {
        IReadOnlyList<ViewerBridgeSession> sessions =
            await _model.GetSessionsAsync().ConfigureAwait(true);
        BridgeSessionSelector.Items.Clear();
        foreach (ViewerBridgeSession session in sessions)
        {
            BridgeSessionSelector.Items.Add(new ComboBoxItem
            {
                Content = session.Description is null
                    ? session.DisplayName
                    : $"{session.DisplayName} \u00b7 {session.Description}",
                Tag = session.Id,
            });
        }
        if (BridgeSessionSelector.ItemCount > 0)
        {
            BridgeSessionSelector.SelectedIndex = 0;
        }
        Apply(_model.CurrentState);
    }
}
