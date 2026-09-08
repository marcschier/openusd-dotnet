// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerCommandDispatcherTests
{
    [Test]
    public async Task CatalogGesturesAndContextButtonsInvokeTheSameEnabledMenuAction()
    {
        using var commands = new ViewerCommandDispatcher();
        var menu = new MenuItem { Header = "Search commands" };
        var button = new Button();
        int calls = 0;
        object? target = null;
        menu.Click += (sender, _) =>
        {
            target = sender;
            calls++;
        };
        commands.Register(ViewerCommandCatalog.Get("view.commandPalette"), menu);
        commands.Connect(button, "view.commandPalette");
        var shortcut = new KeyEventArgs { Key = Key.P, KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift };

        await Assert.That(commands.TryExecuteGesture(shortcut)).IsTrue();
        button.Command!.Execute(null);
        await Assert.That(calls).IsEqualTo(2);
        await Assert.That(target).IsSameReferenceAs(menu);
        menu.IsEnabled = false;
        await Assert.That(commands.TryExecuteGesture(shortcut)).IsFalse();
        await Assert.That(button.IsEffectivelyEnabled).IsFalse();
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task SearchAndInvocationUseTheCurrentMenuStateAndTheOriginalClickTarget()
    {
        var item = new MenuItem { Header = "_Stage panel", ToggleType = MenuItemToggleType.CheckBox };
        var parent = new Border { IsEnabled = false, Child = item };
        object? invokedSender = null;
        int calls = 0;
        item.Click += (sender, _) =>
        {
            invokedSender = sender;
            calls++;
        };
        using var commands = new ViewerCommandDispatcher();
        commands.Register(ViewerCommandCatalog.Get("view.stagePanel"), item);

        ViewerCommandState unavailable = commands.Search("show stage").Single();
        await Assert.That(unavailable.Id).IsEqualTo("view.stagePanel");
        await Assert.That(unavailable.AccessibleName).IsEqualTo("Show stage panel");
        await Assert.That(unavailable.IsEnabled).IsFalse();
        await Assert.That(commands.TryExecute("view.stagePanel")).IsFalse();
        await Assert.That(calls).IsEqualTo(0);

        parent.IsEnabled = true;
        await Assert.That(commands.TryExecute("view.stagePanel")).IsTrue();
        await Assert.That(invokedSender).IsSameReferenceAs(item);
        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(item.IsChecked).IsTrue();
        await Assert.That(commands.Search("view stage").Single().IsChecked).IsTrue();
        await Assert.That(AutomationProperties.GetName(item)).IsEqualTo("Show stage panel");

        parent.IsVisible = false;
        await Assert.That(commands.Search("stage")).IsEmpty();
        await Assert.That(commands.TryExecute("view.stagePanel")).IsFalse();
        await Assert.That(calls).IsEqualTo(1);
    }
}
