// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Asserts that a toolbar control the overflow deferred is still operable from the menu that
/// replaced it, because a menu entry that does nothing when clicked is a control the user lost.
/// </summary>
/// <remarks>
/// Avalonia registers a control type's properties on first construction and that registration is
/// not safe to run concurrently for the same type, so these tests build their controls one at a
/// time.
/// </remarks>
[NotInParallel("AvaloniaControls")]
public sealed class ViewerToolbarOverflowMenuTests
{
    [Test]
    public async Task AnOverflowedButtonContributesOneEntryThatPressesIt()
    {
        var button = new Button { Content = "Bake..." };
        int clicks = 0;

        MenuItem item = ViewerToolbarOverflowMenu.CreateItem(
            button,
            "Bake...",
            7,
            (_, _) => clicks++);

        await Assert.That(item.Header).IsEqualTo("Bake...");
        await Assert.That(item.Tag).IsEqualTo(7);
        await Assert.That(AutomationProperties.GetName(item)).IsEqualTo("Bake...");
        await Assert.That(item.ItemsSource is null).IsTrue();

        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        await Assert.That(clicks).IsEqualTo(1);
    }

    [Test]
    public async Task AnOverflowedSpeedSelectorContributesOneEntryPerSpeed()
    {
        ComboBox selector = NewSpeedSelector();

        MenuItem item = ViewerToolbarOverflowMenu.CreateItem(selector, "Speed", 5, (_, _) => { });
        MenuItem[] choices = Choices(item);

        await Assert.That(choices.Length).IsEqualTo(selector.ItemCount);
        await Assert.That(choices[0].Header).IsEqualTo("0.25x");
        await Assert.That(AutomationProperties.GetName(choices[0])).IsEqualTo("Speed 0.25x");
        await Assert.That(choices[2].IsChecked).IsTrue();
        await Assert.That(choices[0].IsChecked).IsFalse();
    }

    [Test]
    public async Task ClickingTheOverflowedSpeedEntryActuallyChangesTheSpeed()
    {
        ComboBox selector = NewSpeedSelector();
        int closed = 0;

        MenuItem item = ViewerToolbarOverflowMenu.CreateItem(
            selector,
            "Speed",
            5,
            (_, _) => { },
            () => closed++);
        MenuItem chosen = Choices(item)[4];
        var args = new RoutedEventArgs(MenuItem.ClickEvent);
        chosen.RaiseEvent(args);

        // The whole point of the entry: the deferred control changed, and the menu that hosted it
        // closed behind the choice.
        await Assert.That(selector.SelectedIndex).IsEqualTo(4);
        await Assert.That(args.Handled).IsTrue();
        await Assert.That(closed).IsEqualTo(1);
    }

    [Test]
    public async Task ADisabledControlContributesADisabledEntry()
    {
        ComboBox selector = NewSpeedSelector();
        selector.IsEnabled = false;

        MenuItem item = ViewerToolbarOverflowMenu.CreateItem(selector, "Speed", 5, (_, _) => { });

        await Assert.That(item.IsEnabled).IsFalse();
        foreach (MenuItem choice in Choices(item))
        {
            await Assert.That(choice.IsEnabled).IsFalse();
        }
    }

    [Test]
    public async Task EveryPhysicsSpeedIsReachableFromTheNarrowestToolbar()
    {
        ViewerToolbarItem[] toolbar =
        [
            new("PhysicsEnableButton", "Physics", 88),
            new("PhysicsPlayPauseButton", "Play", 72),
            new("PhysicsStopButton", "Stop", 72),
            new("PhysicsStepButton", "Step", 72),
            new("PhysicsLoopCheckBox", "Loop", 72),
            new("PhysicsSpeedSelector", "Speed", 96),
            new("PhysicsPreviewCheckBox", "Apply Preview", 132),
            new("PhysicsBakeButton", "Bake...", 84),
        ];

        // A width narrow enough to defer the speed selector is exactly where the menu has to work.
        ViewerToolbarOverflowPlan plan = ViewerToolbarOverflowPlanner.Plan(toolbar, 200d);
        await Assert.That(plan.IsVisible(5)).IsFalse();

        ComboBox selector = NewSpeedSelector();
        MenuItem item = ViewerToolbarOverflowMenu.CreateItem(
            selector,
            toolbar[5].Label,
            5,
            (_, _) => { });
        MenuItem[] choices = Choices(item);

        for (int index = 0; index < choices.Length; index++)
        {
            choices[index].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Assert.That(selector.SelectedIndex).IsEqualTo(index);
        }
    }

    private static ComboBox NewSpeedSelector()
    {
        var selector = new ComboBox
        {
            ItemsSource = new List<ComboBoxItem>
            {
                new() { Content = "0.25x" },
                new() { Content = "0.5x" },
                new() { Content = "1x" },
                new() { Content = "2x" },
                new() { Content = "4x" },
            },
            SelectedIndex = 2,
        };
        return selector;
    }

    private static MenuItem[] Choices(MenuItem item) =>
        item.ItemsSource is IEnumerable<MenuItem> items
            ? [.. items]
            : [];
}
