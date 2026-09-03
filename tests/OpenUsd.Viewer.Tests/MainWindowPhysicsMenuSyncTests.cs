// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Exercises <see cref="MainWindow.SetPhysicsCommandState"/> directly against constructed
/// controls and physics snapshots, so a regression in Physics transport button/menu-item
/// synchronization (Play/Pause, Stop, Step, Bake) is caught by an actual state assertion
/// rather than only by a source-string check that a call site exists.
/// </summary>
/// <remarks>
/// Avalonia registers a control type's properties on first construction and that registration
/// is not safe to run concurrently for the same type, so this class runs serialized against
/// other tests that construct raw Avalonia controls (see
/// <c>ViewerToolbarOverflowMenuTests</c>).
/// </remarks>
[NotInParallel("AvaloniaControls")]
public sealed class MainWindowPhysicsMenuSyncTests
{
    [Test]
    public async Task AvailableCommandEnablesBothTheButtonAndTheMenuItemWithTheReadyTip()
    {
        var button = new Button { IsEnabled = false };
        var menuItem = new MenuItem { IsEnabled = false };
        ViewerPhysicsStatusSnapshot playable = ViewerPhysicsStatusSnapshot.Disabled with
        {
            IsEnabled = true,
            IsPlaying = false,
        };

        bool changed = MainWindow.SetPhysicsCommandState(
            button, menuItem, playable, ViewerPhysicsCommand.Play, "Play or pause (K)");

        await Assert.That(changed).IsTrue();
        await Assert.That(button.IsEnabled).IsTrue();
        await Assert.That(menuItem.IsEnabled).IsTrue();
        await Assert.That(ToolTip.GetTip(button)).IsEqualTo("Play or pause (K)");
        await Assert.That(ToolTip.GetTip(menuItem)).IsEqualTo("Play or pause (K)");
    }

    [Test]
    public async Task DisabledPhysicsRefusesEveryTransportCommandAndExplainsWhyOnBoth()
    {
        var button = new Button();
        var menuItem = new MenuItem();
        ViewerPhysicsStatusSnapshot disabled = ViewerPhysicsStatusSnapshot.Disabled;

        MainWindow.SetPhysicsCommandState(
            button, menuItem, disabled, ViewerPhysicsCommand.Play, "Play or pause (K)");

        await Assert.That(button.IsEnabled).IsFalse();
        await Assert.That(menuItem.IsEnabled).IsFalse();
        string? tip = ToolTip.GetTip(button) as string;
        await Assert.That(tip).IsEqualTo("Enable physics for this stage first.");
        await Assert.That(ToolTip.GetTip(menuItem)).IsEqualTo(tip);
    }

    [Test]
    public async Task PlayIsUnavailableWhileAlreadyPlayingButPauseIs()
    {
        var playButton = new Button();
        var playMenuItem = new MenuItem();
        var pauseButton = new Button();
        var pauseMenuItem = new MenuItem();
        ViewerPhysicsStatusSnapshot playing = ViewerPhysicsStatusSnapshot.Disabled with
        {
            IsEnabled = true,
            IsPlaying = true,
        };

        MainWindow.SetPhysicsCommandState(
            playButton, playMenuItem, playing, ViewerPhysicsCommand.Play, "Play");
        MainWindow.SetPhysicsCommandState(
            pauseButton, pauseMenuItem, playing, ViewerPhysicsCommand.Pause, "Pause");

        await Assert.That(playButton.IsEnabled).IsFalse();
        await Assert.That(playMenuItem.IsEnabled).IsFalse();
        await Assert.That(pauseButton.IsEnabled).IsTrue();
        await Assert.That(pauseMenuItem.IsEnabled).IsTrue();
    }

    [Test]
    public async Task ABusyTransportDisablesStopAndStepOnBothControlsWithABusyExplanation()
    {
        var stopButton = new Button();
        var stopMenuItem = new MenuItem();
        ViewerPhysicsStatusSnapshot busy = ViewerPhysicsStatusSnapshot.Disabled with
        {
            IsEnabled = true,
            IsBusy = true,
        };

        MainWindow.SetPhysicsCommandState(
            stopButton, stopMenuItem, busy, ViewerPhysicsCommand.Stop, "Stop");

        await Assert.That(stopButton.IsEnabled).IsFalse();
        await Assert.That(stopMenuItem.IsEnabled).IsFalse();
        await Assert.That(ToolTip.GetTip(stopButton))
            .IsEqualTo("The physics worker is busy; the command runs once it finishes.");
        await Assert.That(ToolTip.GetTip(stopMenuItem))
            .IsEqualTo(ToolTip.GetTip(stopButton));
    }

    [Test]
    public async Task ANullMenuItemIsAcceptedAndOnlyTheControlIsUpdated()
    {
        // Bake is itself the menu item today (there is no separate toolbar button), so the
        // call site passes null for menuItem; this must not throw or silently do nothing to
        // the control that was actually passed.
        var bakeMenuItem = new MenuItem { IsEnabled = false };
        ViewerPhysicsStatusSnapshot ready = ViewerPhysicsStatusSnapshot.Disabled with
        {
            IsEnabled = true,
        };

        bool changed = MainWindow.SetPhysicsCommandState(
            bakeMenuItem, menuItem: null, ready, ViewerPhysicsCommand.Bake, "Bake...");

        await Assert.That(changed).IsTrue();
        await Assert.That(bakeMenuItem.IsEnabled).IsTrue();
        await Assert.That(ToolTip.GetTip(bakeMenuItem)).IsEqualTo("Bake...");
    }

    [Test]
    public async Task RepeatedCallsWithNoStateChangeReportUnchangedButKeepBothControlsInSync()
    {
        var button = new Button { IsEnabled = false };
        var menuItem = new MenuItem { IsEnabled = false };
        ViewerPhysicsStatusSnapshot playable = ViewerPhysicsStatusSnapshot.Disabled with
        {
            IsEnabled = true,
        };

        bool first = MainWindow.SetPhysicsCommandState(
            button, menuItem, playable, ViewerPhysicsCommand.StepOneFrame, "Step");
        bool second = MainWindow.SetPhysicsCommandState(
            button, menuItem, playable, ViewerPhysicsCommand.StepOneFrame, "Step");

        await Assert.That(first).IsTrue();
        await Assert.That(second).IsFalse();
        await Assert.That(button.IsEnabled).IsTrue();
        await Assert.That(menuItem.IsEnabled).IsTrue();
    }

    [Test]
    public async Task ANeedsRebuildStateDisablesTransportCommandsOnBothControls()
    {
        var stepButton = new Button();
        var stepMenuItem = new MenuItem();
        ViewerPhysicsStatusSnapshot faulted = ViewerPhysicsStatusSnapshot.Disabled with
        {
            IsEnabled = true,
            Status = ViewerPhysicsTransportStatus.Disabled with
            {
                State = ViewerPhysicsRunState.Faulted,
            },
        };

        MainWindow.SetPhysicsCommandState(
            stepButton, stepMenuItem, faulted, ViewerPhysicsCommand.StepOneFrame, "Step");

        await Assert.That(stepButton.IsEnabled).IsFalse();
        await Assert.That(stepMenuItem.IsEnabled).IsFalse();
        await Assert.That(ToolTip.GetTip(stepButton))
            .IsEqualTo("The built world is stale; rebuild it before simulating again.");
    }
}
