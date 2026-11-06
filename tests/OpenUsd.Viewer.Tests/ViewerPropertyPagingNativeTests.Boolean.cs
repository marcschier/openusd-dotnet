// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerPropertyPagingNativeTests
{
    private static async Task ExerciseBooleanPropertiesAsync(
        MainWindow main, ViewerStageSession session, TextBox query, CancellationToken timeout)
    {
        foreach (string name in new[] { "inputs:normalize", "inputs:enableColorTemperature" })
        {
            await FilterAttributeAsync(main, query, name, timeout, "/Light");
            await WaitAsync(() => PropertyButtons(main).Single().IsEnabled, timeout);
            PropertyButtons(main).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => main.OwnedWindows.OfType<PropertyEditWindow>().Any(), timeout);
            PropertyEditWindow edit = main.OwnedWindows.OfType<PropertyEditWindow>().Single();
            await WaitAsync(() => Required<Button>(edit, "ClearPropertyButton").IsEnabled, timeout);
            CheckBox boolean = Required<CheckBox>(edit, "PropertyBooleanValue");
            await Assert.That(AutomationProperties.GetAutomationId(boolean)).IsEqualTo("edit.propertyBooleanValue");
            await Assert.That(boolean.IsVisible && boolean.IsEnabled && boolean.IsThreeState).IsTrue();
            await Assert.That(Required<TextBox>(edit, "PropertyValue").IsVisible).IsFalse();
            await Assert.That(boolean.IsChecked).IsNull();
            await Assert.That(Required<Button>(edit, "SetPropertyButton").IsEnabled).IsFalse();
            await Assert.That(Required<TextBlock>(edit, "PropertyTargetState").Text).Contains("Default: Absent");
            await Assert.That(await session.Scheduler.InvokeAsync(
                stage => stage.GetPrim("/Light").GetAttribute(name).IsAuthored, timeout)).IsFalse();
            Required<TextBox>(edit, "PropertyValue").Text = "true";
            Click(edit, "SetPropertyButton");
            await Assert.That(Required<TextBlock>(edit, "PropertyEditStatus").Text)
                .StartsWith("Choose true or false before Set.");
            await Assert.That(await session.Scheduler.InvokeAsync(
                stage => stage.GetPrim("/Light").GetAttribute(name).IsAuthored, timeout)).IsFalse();

            foreach (bool value in new[] { true, false })
            {
                boolean.IsChecked = value;
                await Assert.That(Required<Button>(edit, "SetPropertyButton").IsEnabled).IsTrue();
                await Assert.That(await session.Scheduler.InvokeAsync(
                    stage => stage.GetPrim("/Light").GetBool(name), timeout)).IsFalse();
                Click(edit, "SetPropertyButton");
                await WaitAsync(() => Required<Button>(edit, "ClearPropertyButton").IsEnabled &&
                    Required<TextBlock>(edit, "PropertyEditStatus").Text ==
                    "Review edit applied.", timeout);
                await Assert.That(await session.Scheduler.InvokeAsync(
                    stage => stage.GetPrim("/Light").GetBool(name), timeout)).IsEqualTo(value);
                await Assert.That(boolean.IsChecked).IsEqualTo((bool?)value);
                boolean.IsChecked = !value;
                Click(edit, "RefreshPropertyButton");
                await WaitAsync(() => Required<Button>(edit, "RefreshPropertyButton").IsEnabled, timeout);
                await Assert.That(boolean.IsChecked).IsEqualTo((bool?)value);
                Click(edit, "ClearPropertyButton");
                await WaitAsync(() => Required<Button>(edit, "ClearPropertyButton").IsEnabled &&
                    Required<TextBlock>(edit, "PropertyEditStatus").Text ==
                    "Review edit applied.", timeout);
                await Assert.That(boolean.IsChecked).IsNull();
                await Assert.That(Required<Button>(edit, "SetPropertyButton").IsEnabled).IsFalse();
                await Assert.That(await session.Scheduler.InvokeAsync(
                    stage => stage.GetPrim("/Light").GetAttribute(name).IsAuthored, timeout)).IsFalse();
            }

            Click(edit, "BlockPropertyButton");
            await WaitAsync(() => Required<Button>(edit, "ClearPropertyButton").IsEnabled &&
                Required<TextBlock>(edit, "PropertyEditStatus").Text ==
                "Review edit applied.", timeout);
            await Assert.That(Required<TextBlock>(edit, "PropertyTargetState").Text).Contains("Default: Block");
            await Assert.That(boolean.IsChecked).IsNull();
            await Assert.That(Required<Button>(edit, "SetPropertyButton").IsEnabled).IsFalse();
            boolean.IsChecked = true;
            boolean.IsChecked = null;
            await Assert.That(Required<Button>(edit, "SetPropertyButton").IsEnabled).IsFalse();
            Click(edit, "RefreshPropertyButton");
            await WaitAsync(() => Required<Button>(edit, "RefreshPropertyButton").IsEnabled, timeout);
            await Assert.That(Required<TextBlock>(edit, "PropertyTargetState").Text).Contains("Default: Block");
            Click(edit, "ClearPropertyButton");
            await WaitAsync(() => Required<Button>(edit, "ClearPropertyButton").IsEnabled &&
                Required<TextBlock>(edit, "PropertyEditStatus").Text ==
                "Review edit applied.", timeout);

            ComboBox selector = Required<ComboBox>(edit, "PropertySelector");
            selector.SelectedIndex = selector.Items.OfType<string>().ToList().IndexOf("inputs:intensity (float)");
            await WaitAsync(() => Required<Button>(edit, "ClearPropertyButton").IsEnabled, timeout);
            await Assert.That(boolean.IsVisible).IsFalse();
            await Assert.That(Required<TextBox>(edit, "PropertyValue").IsVisible).IsTrue();
            selector.SelectedIndex = selector.Items.OfType<string>().ToList().IndexOf($"{name} (bool)");
            await WaitAsync(() => Required<Button>(edit, "ClearPropertyButton").IsEnabled, timeout);
            await Assert.That(boolean.IsVisible).IsTrue();
            await Assert.That(boolean.IsChecked).IsNull();
            await Assert.That(Required<Button>(edit, "SetPropertyButton").IsEnabled).IsFalse();
            Required<ComboBox>(edit, "PropertyFieldSelector").SelectedIndex = 1;
            await WaitAsync(() => Required<Button>(edit, "ClearPropertyButton").IsEnabled, timeout);
            await Assert.That(boolean.IsChecked).IsNull();
            await Assert.That(Required<Button>(edit, "SetPropertyButton").IsEnabled).IsFalse();
            boolean.IsChecked = true;
            Click(edit, "SetPropertyButton");
            await WaitAsync(() => Required<Button>(edit, "ClearPropertyButton").IsEnabled &&
                Required<TextBlock>(edit, "PropertyEditStatus").Text ==
                "Review edit applied.", timeout);
            await Assert.That(Required<TextBlock>(edit, "PropertyTargetState").Text).Contains("TimeSample: true");
            await Assert.That(await session.Scheduler.InvokeAsync(
                stage => stage.GetPrim("/Light").GetBool(name, 5), timeout)).IsTrue();
            await Assert.That(await session.Scheduler.InvokeAsync(
                stage => stage.GetPrim("/Light").GetBool(name), timeout)).IsFalse();
            await edit.CloseAsync();
            await WaitAsync(() => Required<MenuItem>(main, "EditUndoMenuItem").IsEnabled, timeout);
            Required<MenuItem>(main, "EditUndoMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => Required<MenuItem>(main, "EditRedoMenuItem").IsEnabled, timeout);
            await Assert.That(await session.Scheduler.InvokeAsync(
                stage => stage.GetPrim("/Light").GetAttribute(name).IsAuthored, timeout)).IsFalse();
            Required<MenuItem>(main, "EditRedoMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => Required<MenuItem>(main, "EditUndoMenuItem").IsEnabled, timeout);
            await Assert.That(await session.Scheduler.InvokeAsync(
                stage => stage.GetPrim("/Light").GetBool(name, 5), timeout)).IsTrue();
            Required<MenuItem>(main, "EditUndoMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => Required<MenuItem>(main, "EditRedoMenuItem").IsEnabled, timeout);
            await Assert.That(await session.Scheduler.InvokeAsync(
                stage => stage.GetPrim("/Light").GetAttribute(name).IsAuthored, timeout)).IsFalse();
        }
    }
}
