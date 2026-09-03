// Copyright (c) marcschier. Licensed under the MIT License.

using System.Xml.Linq;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Covers the Viewer's host-injected bridge seam without a window, a dispatcher, or a
/// transport: what the menu and status bar show for a given provider, how commands are gated,
/// what happens when a provider throws, is cancelled, or floods the marshalling queue, and
/// that nothing that crosses the seam can carry a credential.
/// </summary>
public sealed class ViewerBridgeConnectionTests
{
    [Test]
    public async Task NoInjectedProviderLeavesTheBridgeSurfaceAbsentRatherThanDisabled()
    {
        using var model = new ViewerBridgeConnectionModel(
            provider: null,
            static action => action(),
            static _ => { });

        ViewerBridgeViewState state = model.CurrentState;

        await Assert.That(model.HasProvider).IsFalse();
        await Assert.That(state.MenuVisible).IsFalse();
        await Assert.That(state.MenuEnabled).IsFalse();
        await Assert.That(state.StatusVisible).IsFalse();
        await Assert.That(state.CanConnect).IsFalse();
        await Assert.That(state.CanDisconnect).IsFalse();
        await Assert.That(state.CanResync).IsFalse();
        await Assert.That(await model.ConnectAsync("any")).IsFalse();
        await Assert.That(await model.DisconnectAsync()).IsFalse();
        await Assert.That(await model.ResyncAsync()).IsFalse();
        await Assert.That(await model.GetSessionsAsync()).IsEmpty();
    }

    [Test]
    public async Task AnInjectedButUnavailableProviderKeepsTheEntryVisibleAndDisabled()
    {
        var provider = new FakeBridgeProvider { IsAvailable = false };
        using var model = CreateModel(provider, out _);

        ViewerBridgeViewState state = model.CurrentState;

        await Assert.That(state.MenuVisible).IsTrue();
        await Assert.That(state.MenuEnabled).IsFalse();
        await Assert.That(state.StatusVisible).IsFalse();
        await Assert.That(state.CanConnect).IsFalse();
        await Assert.That(state.MenuToolTip).Contains("not available");

        // A session that is still active stays reported even after the provider reports
        // itself unavailable: hiding it would lose the one fact an operator needs, which is
        // that something is still connected on their behalf.
        provider.Raise(provider.Status with { State = ViewerBridgeConnectionState.Streaming });

        await Assert.That(model.CurrentState.StatusVisible).IsTrue();
        await Assert.That(model.CurrentState.MenuEnabled).IsFalse();
        await Assert.That(model.CurrentState.CanDisconnect).IsFalse();
    }

    [Test]
    public async Task AnAvailableProviderEnablesConnectAndNothingElseBeforeConnecting()
    {
        var provider = new FakeBridgeProvider();
        using var model = CreateModel(provider, out _);

        ViewerBridgeViewState state = model.CurrentState;

        await Assert.That(state.MenuVisible).IsTrue();
        await Assert.That(state.MenuEnabled).IsTrue();
        await Assert.That(state.CanConnect).IsTrue();
        await Assert.That(state.CanDisconnect).IsFalse();
        await Assert.That(state.CanResync).IsFalse();

        // A provider is present but nothing was ever configured, so the status bar stays out
        // of the way of a viewer that is only opening local files.
        await Assert.That(state.StatusVisible).IsFalse();
    }

    [Test]
    public async Task ConnectingRunsTheProviderAndReportsStreamingInTheStatusBar()
    {
        var provider = new FakeBridgeProvider();
        provider.OnConnect = request =>
        {
            provider.Status = provider.Status with
            {
                State = ViewerBridgeConnectionState.Streaming,
                SessionId = request.SessionId,
                AppliedUpdateCount = 12,
                ConnectAttemptCount = 1
            };
            return ValueTask.CompletedTask;
        };
        using var model = CreateModel(provider, out List<ViewerBridgeViewState> published);

        bool connected = await model.ConnectAsync("stage-a");
        ViewerBridgeViewState state = model.CurrentState;

        await Assert.That(connected).IsTrue();
        await Assert.That(provider.ConnectRequests.Count).IsEqualTo(1);
        await Assert.That(provider.ConnectRequests[0]).IsEqualTo("stage-a");
        await Assert.That(state.Status.State).IsEqualTo(ViewerBridgeConnectionState.Streaming);
        await Assert.That(state.StatusVisible).IsTrue();
        await Assert.That(state.StatusText).Contains("streaming");
        await Assert.That(state.StatusText).Contains("stage-a");
        await Assert.That(state.CanConnect).IsFalse();
        await Assert.That(state.CanDisconnect).IsTrue();
        await Assert.That(state.CanResync).IsTrue();
        await Assert.That(state.ErrorMessage).IsNull();

        // Busy is published before the command runs and cleared after it, so a slow provider
        // cannot leave the dialog's buttons enabled while a command is in flight.
        await Assert.That(published.Any(entry => entry.Busy)).IsTrue();
        await Assert.That(published[^1].Busy).IsFalse();
    }

    [Test]
    public async Task DisconnectingClearsTheStatusBarAgain()
    {
        var provider = new FakeBridgeProvider();
        provider.OnConnect = _ =>
        {
            provider.Status = provider.Status with
            {
                State = ViewerBridgeConnectionState.Streaming,
                SessionId = "live"
            };
            return ValueTask.CompletedTask;
        };
        provider.OnDisconnect = () =>
        {
            provider.Status = ViewerBridgeStatus.Disconnected;
            return ValueTask.CompletedTask;
        };
        using var model = CreateModel(provider, out _);

        _ = await model.ConnectAsync(null);
        bool disconnected = await model.DisconnectAsync();
        ViewerBridgeViewState state = model.CurrentState;

        await Assert.That(disconnected).IsTrue();
        await Assert.That(state.Status.State).IsEqualTo(ViewerBridgeConnectionState.Disconnected);
        await Assert.That(state.StatusVisible).IsFalse();
        await Assert.That(state.CanConnect).IsTrue();
        await Assert.That(state.CanDisconnect).IsFalse();
    }

    [Test]
    public async Task ResyncIsRefusedUntilASessionIsActuallyStreaming()
    {
        var provider = new FakeBridgeProvider();
        using var model = CreateModel(provider, out _);

        bool refused = await model.ResyncAsync();

        await Assert.That(refused).IsFalse();
        await Assert.That(provider.ResyncCount).IsEqualTo(0);

        provider.Raise(provider.Status with { State = ViewerBridgeConnectionState.Streaming });
        bool accepted = await model.ResyncAsync();

        await Assert.That(accepted).IsTrue();
        await Assert.That(provider.ResyncCount).IsEqualTo(1);
    }

    [Test]
    public async Task ProviderStatusEventsAreMarshalledAsDetachedSnapshots()
    {
        var provider = new FakeBridgeProvider();
        List<Action> posted = [];
        List<ViewerBridgeViewState> published = [];
        using var model = new ViewerBridgeConnectionModel(
            provider,
            posted.Add,
            published.Add);

        provider.Raise(new ViewerBridgeStatus(
            ViewerBridgeConnectionState.Connecting,
            SessionId: "s1",
            Endpoint: null,
            ConnectAttemptCount: 1,
            AppliedUpdateCount: 0,
            PendingOutboundCount: 0,
            TimestampUtc: DateTimeOffset.UnixEpoch,
            Detail: null));
        provider.Raise(new ViewerBridgeStatus(
            ViewerBridgeConnectionState.Streaming,
            SessionId: "s1",
            Endpoint: null,
            ConnectAttemptCount: 1,
            AppliedUpdateCount: 4,
            PendingOutboundCount: 0,
            TimestampUtc: DateTimeOffset.UnixEpoch,
            Detail: null));

        // Nothing has run on the "UI thread" yet: the provider's own thread only enqueued.
        await Assert.That(published).IsEmpty();
        await Assert.That(posted.Count).IsEqualTo(1);

        posted[0]();

        await Assert.That(published.Select(entry => entry.Status.State)).IsEquivalentTo(
        [
            ViewerBridgeConnectionState.Connecting,
            ViewerBridgeConnectionState.Streaming,
        ]);
        await Assert.That(published[^1].Status.AppliedUpdateCount).IsEqualTo(4);
        await Assert.That(published[^1].DroppedStatusEventCount).IsEqualTo(0);
    }

    [Test]
    public async Task TheMarshallingQueueIsBoundedAndReportsWhatItDropped()
    {
        var provider = new FakeBridgeProvider();
        List<Action> posted = [];
        List<ViewerBridgeViewState> published = [];
        using var model = new ViewerBridgeConnectionModel(
            provider,
            posted.Add,
            published.Add,
            eventCapacity: 4);

        for (int attempt = 1; attempt <= 20; attempt++)
        {
            provider.Raise(provider.Status with
            {
                State = ViewerBridgeConnectionState.Connecting,
                ConnectAttemptCount = attempt
            });
        }

        await Assert.That(posted.Count).IsEqualTo(1);
        posted[0]();

        await Assert.That(published.Count).IsEqualTo(4);
        await Assert.That(model.DroppedStatusEventCount).IsEqualTo(16);

        // The bound drops the oldest, so the operator always sees the newest truth rather than
        // a stale prefix of a burst.
        await Assert.That(published[^1].Status.ConnectAttemptCount).IsEqualTo(20);
        await Assert.That(published[0].Status.ConnectAttemptCount).IsEqualTo(17);
    }

    [Test]
    public async Task AProviderFailureBecomesARedactedMessageInsteadOfAnException()
    {
        var provider = new FakeBridgeProvider
        {
            OnConnect = _ => throw new InvalidOperationException(
                "refused by https://operator:sup3rsecret@bridge.example.com:8443/session?token=abc"),
        };
        using var model = CreateModel(provider, out _);

        bool connected = await model.ConnectAsync("s1");
        ViewerBridgeViewState state = model.CurrentState;

        await Assert.That(connected).IsFalse();
        await Assert.That(state.ErrorMessage).IsNotNull();
        await Assert.That(state.ErrorMessage!).StartsWith("Connect failed:");
        await Assert.That(state.ErrorMessage!).DoesNotContain("sup3rsecret");
        await Assert.That(state.ErrorMessage!).DoesNotContain("operator:");
        await Assert.That(state.ErrorMessage!).DoesNotContain("token=");
        await Assert.That(state.ErrorMessage!).Contains(nameof(InvalidOperationException));
        await Assert.That(state.Busy).IsFalse();
        await Assert.That(state.CanConnect).IsTrue();
    }

    [Test]
    public async Task ACancelledCommandIsReportedAsCancellationRatherThanAFault()
    {
        var provider = new FakeBridgeProvider
        {
            OnConnect = _ => throw new OperationCanceledException(),
        };
        using var model = CreateModel(provider, out _);

        bool connected = await model.ConnectAsync(null);

        await Assert.That(connected).IsFalse();
        await Assert.That(model.CurrentState.ErrorMessage).IsEqualTo("Connect was cancelled.");
    }

    [Test]
    public async Task DisposalUnsubscribesCancelsAndRefusesLaterCommands()
    {
        var provider = new FakeBridgeProvider();
        List<ViewerBridgeViewState> published = [];
        var model = new ViewerBridgeConnectionModel(
            provider,
            static action => action(),
            published.Add);
        CancellationToken observed = default;
        provider.OnDisconnect = () => ValueTask.CompletedTask;
        provider.OnConnect = _ => ValueTask.CompletedTask;
        provider.OnResync = token =>
        {
            observed = token;
            return ValueTask.CompletedTask;
        };
        provider.Raise(provider.Status with { State = ViewerBridgeConnectionState.Streaming });
        _ = await model.ResyncAsync();
        int publishedBeforeDisposal = published.Count;

        model.Dispose();
        provider.Raise(provider.Status with { State = ViewerBridgeConnectionState.Faulted });

        await Assert.That(observed.IsCancellationRequested).IsTrue();
        await Assert.That(provider.SubscriberCount).IsEqualTo(0);
        await Assert.That(published.Count).IsEqualTo(publishedBeforeDisposal);
        await Assert.That(await model.ConnectAsync(null)).IsFalse();
        await Assert.That(await model.GetSessionsAsync()).IsEmpty();
        model.Dispose();
    }

    [Test]
    public async Task OnlyOneCommandRunsAtATime()
    {
        var provider = new FakeBridgeProvider();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.OnConnectAsync = async _ => await release.Task;
        using var model = CreateModel(provider, out _);

        Task<bool> first = model.ConnectAsync("a");
        bool second = await model.ConnectAsync("b");
        release.SetResult();

        await Assert.That(second).IsFalse();
        await Assert.That(await first).IsTrue();
        await Assert.That(provider.ConnectRequests.Count).IsEqualTo(1);
        await Assert.That(provider.ConnectRequests[0]).IsEqualTo("a");
    }

    [Test]
    public async Task SessionsAreBoundedSanitizedAndSurviveAProviderFailure()
    {
        var provider = new FakeBridgeProvider();
        provider.Sessions.AddRange(Enumerable
            .Range(0, ViewerBridgeLimits.MaxSessionCount + 10)
            .Select(index => new ViewerBridgeSession(
                $"session-{index}",
                new string('x', ViewerBridgeLimits.MaxTextLength + 50))));
        using var model = CreateModel(provider, out _);

        IReadOnlyList<ViewerBridgeSession> sessions = await model.GetSessionsAsync();

        await Assert.That(sessions.Count).IsEqualTo(ViewerBridgeLimits.MaxSessionCount);
        await Assert.That(sessions[0].DisplayName.Length)
            .IsLessThanOrEqualTo(ViewerBridgeLimits.MaxTextLength);

        provider.OnSessions = () => throw new TimeoutException("session listing timed out");
        IReadOnlyList<ViewerBridgeSession> afterFailure = await model.GetSessionsAsync();

        await Assert.That(afterFailure).IsEmpty();
        await Assert.That(model.CurrentState.ErrorMessage!).StartsWith("Reading sessions failed:");
    }

    [Test]
    public async Task AProviderThatThrowsFromItsOwnPropertiesCannotBreakTheShell()
    {
        var provider = new FakeBridgeProvider
        {
            ThrowFromIsAvailable = true,
            ThrowFromGetStatus = true,
            ThrowFromDisplayName = true,
        };
        using var model = CreateModel(provider, out _);

        ViewerBridgeViewState state = model.CurrentState;

        await Assert.That(state.MenuVisible).IsTrue();
        await Assert.That(state.MenuEnabled).IsFalse();
        await Assert.That(state.ErrorMessage).IsNotNull();
        await Assert.That(model.ProviderDisplayName).IsEqualTo("Omniverse Bridge");
        await Assert.That(state.MenuToolTip).Contains("Omniverse Bridge");
        await Assert.That(state.ErrorMessage!).DoesNotContain("display-name-secret");
    }

    [Test]
    [Arguments("https://operator:secret@bridge.example.com:8443/live?token=abc",
        "https://bridge.example.com:8443/live")]
    [Arguments("http://127.0.0.1:53017", "http://127.0.0.1:53017")]
    [Arguments("https://bridge.example.com/", "https://bridge.example.com")]
    public async Task EndpointRedactionDropsUserInfoQueryAndFragment(string input, string expected)
    {
        await Assert.That(ViewerBridgeEndpoint.Redact(input)).IsEqualTo(expected);
        await Assert.That(ViewerBridgeEndpoint.Redact(new Uri(input))).IsEqualTo(expected);
    }

    [Test]
    public async Task ARedactedEndpointIsWhatReachesTheStatusBar()
    {
        var provider = new FakeBridgeProvider();
        using var model = CreateModel(provider, out _);

        provider.Raise(provider.Status with
        {
            State = ViewerBridgeConnectionState.Streaming,
            Endpoint = "https://operator:secret@bridge.example.com:8443/live?token=abc"
        });

        await Assert.That(model.CurrentState.StatusText).DoesNotContain("secret");
        await Assert.That(model.CurrentState.StatusText).DoesNotContain("token=");
        await Assert.That(model.CurrentState.Status.Endpoint)
            .IsEqualTo("https://bridge.example.com:8443/live");
    }

    [Test]
    public async Task AFailingMarshalHandOffIsCountedRatherThanThrownAtTheProvider()
    {
        var provider = new FakeBridgeProvider();
        using var model = new ViewerBridgeConnectionModel(
            provider,
            static _ => throw new InvalidOperationException("dispatcher is shutting down"),
            static _ => { });

        // The provider raises this from its own transport thread. A Viewer-side defect that
        // escaped here would be blamed on the transport's event reporting.
        provider.Raise(provider.Status with { State = ViewerBridgeConnectionState.Streaming });

        await Assert.That(model.DroppedStatusEventCount).IsEqualTo(1);
        await Assert.That(model.CurrentState.ErrorMessage!)
            .StartsWith("Receiving a bridge status failed:");

        // A later notification is still accepted rather than being wedged by the first failure.
        provider.Raise(provider.Status with { State = ViewerBridgeConnectionState.Faulted });

        await Assert.That(model.DroppedStatusEventCount).IsEqualTo(2);
    }

    [Test]
    public async Task TheDefaultSettingsSurfaceHasNoEndpointOrCredentialInput()
    {
        string markup = await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot(), "src", "OpenUsd.Viewer", "MainWindow.axaml"));
        string dialog = await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot(), "src", "OpenUsd.Viewer", "BridgeConnectionWindow.axaml"));

        foreach (string forbidden in new[]
        {
            "BridgeEndpointInput", "BridgeTokenInput", "BridgeCredentialInput",
            "BridgePasswordInput",
        })
        {
            await Assert.That(markup).DoesNotContain(forbidden);
            await Assert.That(dialog).DoesNotContain(forbidden);
        }

        // The one dialog that exists offers a session choice and three commands, never a place
        // to type an endpoint or a secret.
        await Assert.That(dialog).DoesNotContain("<TextBox");
        await Assert.That(dialog).DoesNotContain("PasswordChar");
        await Assert.That(dialog).Contains("x:Name=\"BridgeSessionSelector\"");
    }

    [Test]
    public async Task TheBridgeSurfaceCarriesAccessibleNamesFromTheCommandCatalog()
    {
        XDocument dialog = XDocument.Parse(await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot(), "src", "OpenUsd.Viewer", "BridgeConnectionWindow.axaml")));

        foreach ((string control, string commandId) in new[]
        {
            ("BridgeConnectButton", ViewerCommandIds.ToolsConnectionsBridgeConnect),
            ("BridgeDisconnectButton", ViewerCommandIds.ToolsConnectionsBridgeDisconnect),
            ("BridgeResyncButton", ViewerCommandIds.ToolsConnectionsBridgeResync),
        })
        {
            XElement element = FindByName(dialog, control);
            string? name = element.Attribute("AutomationProperties.Name")?.Value;
            await Assert.That(name)
                .IsEqualTo(ViewerCommandCatalog.Get(commandId).AccessibleName)
                .Because($"'{control}' must read its accessible name from the catalog");
        }
    }

    [Test]
    public async Task TheStatusBarBridgeIndicatorShipsHiddenWithAnAccessibleName()
    {
        XDocument markup = XDocument.Parse(await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot(), "src", "OpenUsd.Viewer", "MainWindow.axaml")));
        XElement status = FindByName(markup, "BridgeStatus");

        await Assert.That(status.Attribute("IsVisible")?.Value).IsEqualTo("False");
        await Assert.That(status.Attribute("AutomationProperties.Name")?.Value)
            .IsEqualTo("Omniverse Bridge status");
    }

    [Test]
    public async Task TheViewerProjectStillCarriesNoTransportDependency()
    {
        XDocument viewer = XDocument.Load(Path.Combine(
            RepositoryRoot(), "src", "OpenUsd.Viewer", "OpenUsd.Viewer.csproj"));

        foreach (string reference in viewer
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty))
        {
            await Assert.That(reference).DoesNotContain("Grpc");
            await Assert.That(reference).DoesNotContain("Protobuf");
        }

        string[] projectReferences = [.. viewer
            .Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(
                element.Attribute("Include")?.Value) ?? string.Empty)];
        foreach (string reference in projectReferences)
        {
            await Assert.That(reference).DoesNotContain("Bridge");
        }
    }

    private static ViewerBridgeConnectionModel CreateModel(
        FakeBridgeProvider provider,
        out List<ViewerBridgeViewState> published)
    {
        List<ViewerBridgeViewState> states = [];
        published = states;
        return new ViewerBridgeConnectionModel(
            provider,
            static action => action(),
            states.Add);
    }

    private static XElement FindByName(XDocument markup, string name)
    {
        foreach (XElement element in markup.Descendants())
        {
            foreach (XAttribute attribute in element.Attributes())
            {
                if (attribute.Name.LocalName == "Name" &&
                    attribute.Name.NamespaceName.Contains("xaml", StringComparison.Ordinal) &&
                    attribute.Value == name)
                {
                    return element;
                }
            }
        }

        throw new InvalidOperationException($"No control named '{name}' exists.");
    }

    private static string RepositoryRoot()
    {
        string current = Environment.CurrentDirectory;
        if (File.Exists(Path.Combine(current, "OpenUsd.slnx")))
        {
            return current;
        }

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }

    /// <summary>
    /// A provider that behaves exactly as badly as the seam allows, so the model's guarantees
    /// are proven against misbehaviour rather than against a cooperative stub.
    /// </summary>
    private sealed class FakeBridgeProvider : IViewerBridgeConnectionProvider
    {
        private EventHandler<ViewerBridgeStatusChangedEventArgs>? _statusChanged;
        private bool _isAvailable = true;

        public string DisplayName
        {
            get => ThrowFromDisplayName
                ? throw new InvalidOperationException("display-name-secret")
                : "Fake Bridge";
        }

        public bool ThrowFromIsAvailable { get; init; }

        public bool ThrowFromGetStatus { get; init; }

        public bool ThrowFromDisplayName { get; init; }

        public bool IsAvailable
        {
            get => ThrowFromIsAvailable
                ? throw new InvalidOperationException("availability probe failed")
                : _isAvailable;
            init => _isAvailable = value;
        }

        public ViewerBridgeStatus Status { get; set; } = ViewerBridgeStatus.Disconnected;

        public List<ViewerBridgeSession> Sessions { get; } = [];

        public List<string?> ConnectRequests { get; } = [];

        public int ResyncCount { get; private set; }

        public int SubscriberCount { get; private set; }

        public Func<ViewerBridgeConnectRequest, ValueTask>? OnConnect { get; set; }

        public Func<ViewerBridgeConnectRequest, Task>? OnConnectAsync { get; set; }

        public Func<ValueTask>? OnDisconnect { get; set; }

        public Func<CancellationToken, ValueTask>? OnResync { get; set; }

        public Func<IReadOnlyList<ViewerBridgeSession>>? OnSessions { get; set; }

        public event EventHandler<ViewerBridgeStatusChangedEventArgs>? StatusChanged
        {
            add
            {
                _statusChanged += value;
                SubscriberCount++;
            }
            remove
            {
                _statusChanged -= value;
                SubscriberCount--;
            }
        }

        public void Raise(ViewerBridgeStatus status)
        {
            Status = status;
            _statusChanged?.Invoke(this, new ViewerBridgeStatusChangedEventArgs(status));
        }

        public ViewerBridgeStatus GetStatus() => ThrowFromGetStatus
            ? throw new InvalidOperationException("status probe failed")
            : Status;

        public ValueTask<IReadOnlyList<ViewerBridgeSession>> GetSessionsAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(OnSessions is null
                ? (IReadOnlyList<ViewerBridgeSession>)Sessions
                : OnSessions());

        public async ValueTask ConnectAsync(
            ViewerBridgeConnectRequest request,
            CancellationToken cancellationToken = default)
        {
            ConnectRequests.Add(request.SessionId);
            if (OnConnectAsync is not null)
            {
                await OnConnectAsync(request);
                return;
            }
            if (OnConnect is not null)
            {
                await OnConnect(request);
            }
        }

        public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (OnDisconnect is not null)
            {
                await OnDisconnect();
            }
        }

        public async ValueTask ResyncAsync(CancellationToken cancellationToken = default)
        {
            ResyncCount++;
            if (OnResync is not null)
            {
                await OnResync(cancellationToken);
            }
        }
    }
}
