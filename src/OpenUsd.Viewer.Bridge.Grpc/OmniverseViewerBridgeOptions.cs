// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Bridge.Grpc;
using OpenUsd.LiveAuthoring;

namespace OpenUsd.Viewer.Bridge.Grpc;

/// <summary>
/// Everything one bridge session needs, produced by the embedding host when the operator
/// picks that session.
/// </summary>
/// <remarks>
/// The endpoint and the credential provider live on <see cref="Options"/>, which the host
/// builds. This package reads neither: it hands the options straight to
/// <see cref="OmniverseBridgeClient"/> and only ever displays the redacted scheme, host,
/// port, and path. Nothing here is written to disk, logged, or shown in a dialog.
/// </remarks>
public sealed class OmniverseViewerBridgeSessionConfiguration
{
    /// <summary>Initializes a configuration for one session.</summary>
    /// <param name="coordinator">The live-authoring coordinator the session applies into.</param>
    /// <param name="options">The fully configured client options, including credentials.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    public OmniverseViewerBridgeSessionConfiguration(
        LiveAuthoringSessionCoordinator coordinator,
        BridgeClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(options);
        Coordinator = coordinator;
        Options = options;
    }

    /// <summary>Gets the coordinator the session applies snapshots and deltas into.</summary>
    public LiveAuthoringSessionCoordinator Coordinator { get; }

    /// <summary>Gets the client options the host configured for this session.</summary>
    public BridgeClientOptions Options { get; }
}

/// <summary>
/// Configures the Viewer-facing bridge provider.
/// </summary>
/// <remarks>
/// A host declares the sessions it is willing to offer and supplies a factory that builds one
/// on demand. The factory is the seam that keeps configuration out of this package: it runs in
/// the host's own code, with the host's own secret store, at the moment the operator asks for
/// a connection, so no endpoint and no credential is ever held here between connections.
/// </remarks>
public sealed class OmniverseViewerBridgeOptions
{
    /// <summary>The default bound on how long one connect attempt may stay pending.</summary>
    public static readonly TimeSpan DefaultReadyTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the name the Viewer shows for this integration.</summary>
    public string DisplayName { get; set; } = "Omniverse Bridge";

    /// <summary>Gets the sessions the Viewer offers the operator.</summary>
    /// <remarks>
    /// An empty list is valid and means the host exposes no explicit choice; connect then
    /// calls <see cref="SessionFactory"/> with a <see langword="null"/> identifier.
    /// </remarks>
    public IList<ViewerBridgeSession> Sessions { get; } = [];

    /// <summary>
    /// Gets or sets the factory that builds the coordinator and client options for the chosen
    /// session identifier, or for the host default when the identifier is
    /// <see langword="null"/>.
    /// </summary>
    public Func<string?, CancellationToken, ValueTask<OmniverseViewerBridgeSessionConfiguration>>?
        SessionFactory
    { get; set; }

    /// <summary>
    /// Gets or sets how long a connect attempt may stay pending before it is reported as a
    /// failure. Every wait in the Viewer's bridge surface is bounded, because a connect that
    /// never resolves leaves an operator staring at a busy dialog with no way to tell whether
    /// the peer is slow or gone.
    /// </summary>
    public TimeSpan ReadyTimeout { get; set; } = DefaultReadyTimeout;

    /// <summary>Validates the options and throws when one would break a documented guarantee.</summary>
    /// <exception cref="ArgumentException">A value is missing or out of range.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            throw new ArgumentException(
                "A display name is required; it is what the Viewer's menu entry reads.",
                nameof(DisplayName));
        }
        if (SessionFactory is null)
        {
            throw new ArgumentException(
                "A session factory is required. This package deliberately holds no endpoint " +
                "and no credential of its own, so it cannot build a session without the host.",
                nameof(SessionFactory));
        }
        if (Sessions.Count > ViewerBridgeLimits.MaxSessionCount)
        {
            throw new ArgumentException(
                $"At most {ViewerBridgeLimits.MaxSessionCount} sessions can be offered.",
                nameof(Sessions));
        }
        if (ReadyTimeout <= TimeSpan.Zero || ReadyTimeout > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentException(
                "The ready timeout must be positive and no longer than ten minutes.",
                nameof(ReadyTimeout));
        }
    }
}
