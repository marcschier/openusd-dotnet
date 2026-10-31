// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerPropertyPagingNativeTests
{
    private static async Task ExerciseCameraOpticsAsync(
        MainWindow main,
        ViewerStageSession session,
        TextBox query,
        ComboBox group,
        CancellationToken timeout)
    {
        foreach ((string name, string text) in new[]
        {
            ("horizontalApertureOffset", "-2.5"),
            ("verticalApertureOffset", "1.25"),
            ("focusDistance", "12.5"),
            ("fStop", "2.8"),
            ("shutter:open", "-0.12500000000000003"),
            ("shutter:close", "0.37500000000000006"),
            ("exposure", "-2.5"),
            ("exposure:iso", "200"),
            ("exposure:time", "0.125"),
            ("exposure:fStop", "2.5"),
            ("exposure:responsivity", "0.5")
        })
        {
            bool isDouble = name.StartsWith("shutter:", StringComparison.Ordinal);
            double parsed = double.Parse(text, CultureInfo.InvariantCulture);
            double expected = isDouble ? parsed : (float)parsed;
            double fallback = await session.Scheduler.InvokeAsync(
                stage => ViewerCameraPropertyNativeTests.ReadAdditionalOptic(stage, name, 0), timeout);
            query.Text = name;
            string identity = $"edit.property:/Camera.{name}";
            await WaitAsync(() => PropertyButtons(main).Any(
                button => AutomationProperties.GetAutomationId(button) == identity), timeout);
            PropertyEditWindow edit = await openEditorAsync();
            Required<TextBox>(edit, "PropertyValue").Text = text;
            Click(edit, "SetPropertyButton");
            await WaitAsync(() => Required<TextBlock>(edit, "PropertyEditStatus").Text ==
                "Review edit applied.", timeout);
            await edit.CloseAsync();
            await assertCameraValueAsync(expected);
            await historyAsync("EditUndoMenuItem");
            await assertCameraValueAsync(fallback);
            await historyAsync("EditRedoMenuItem");
            await assertCameraValueAsync(expected);

            edit = await openEditorAsync();
            Click(edit, "ClearPropertyButton");
            await WaitAsync(() => Required<TextBlock>(edit, "PropertyEditStatus").Text ==
                "Review edit applied.", timeout);
            await edit.CloseAsync();
            await assertCameraValueAsync(fallback);
            await historyAsync("EditUndoMenuItem");
            await assertCameraValueAsync(expected);
            await historyAsync("EditUndoMenuItem");
            await assertCameraValueAsync(fallback);
            await Assert.That(query.Text).IsEqualTo(name);
            await Assert.That(group.SelectedIndex).IsEqualTo((int)ViewerPropertyGroup.Camera);

            async Task<PropertyEditWindow> openEditorAsync()
            {
                await WaitAsync(() => PropertyButtons(main).Any(
                    candidate => AutomationProperties.GetAutomationId(candidate) == identity && candidate.IsEnabled),
                    timeout);
                Button button = PropertyButtons(main).Single(
                    candidate => AutomationProperties.GetAutomationId(candidate) == identity);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitAsync(() => main.OwnedWindows.OfType<PropertyEditWindow>().Any(), timeout);
                PropertyEditWindow window = main.OwnedWindows.OfType<PropertyEditWindow>().Single();
                await WaitAsync(() => Required<Button>(window, "SetPropertyButton").IsEnabled, timeout);
                string type = isDouble ? "double" : "float";
                await Assert.That(Required<ComboBox>(window, "PropertySelector").SelectedItem?.ToString())
                    .IsEqualTo($"{name} ({type})");
                return window;
            }

            async Task assertCameraValueAsync(double value)
            {
                double actual = await session.Scheduler.InvokeAsync(
                    stage => ViewerCameraPropertyNativeTests.ReadAdditionalOptic(stage, name, 0), timeout);
                await Assert.That(actual).IsEqualTo(value);
            }
        }

        async Task historyAsync(string menu)
        {
            await WaitAsync(() => Required<MenuItem>(main, menu).IsEnabled, timeout);
            Required<MenuItem>(main, menu).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            string opposite = menu == "EditUndoMenuItem" ? "EditRedoMenuItem" : "EditUndoMenuItem";
            await WaitAsync(() => Required<MenuItem>(main, opposite).IsEnabled, timeout);
        }
    }
}
