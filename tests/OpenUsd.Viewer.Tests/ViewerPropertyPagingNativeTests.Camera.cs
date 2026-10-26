// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUsd.Geom;

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
            ("shutter:close", "0.37500000000000006")
        })
        {
            bool isDouble = name.StartsWith("shutter:", StringComparison.Ordinal);
            double parsed = double.Parse(text, CultureInfo.InvariantCulture);
            double expected = isDouble ? parsed : (float)parsed;
            await FilterAttributeAsync(main, query, name, timeout, "/Camera");
            await Assert.That(ValueText(main)).Contains("Value: 0");
            await assertCameraValueAsync(0);
            PropertyEditWindow edit = await openEditorAsync();
            Required<TextBox>(edit, "PropertyValue").Text = text;
            Click(edit, "SetPropertyButton");
            await WaitAsync(() => Required<TextBlock>(edit, "PropertyEditStatus").Text ==
                "Review edit applied.", timeout);
            await edit.CloseAsync();
            await assertCameraValueAsync(expected);
            await historyAsync("EditUndoMenuItem");
            await assertCameraValueAsync(0);
            await WaitAsync(() => ValueText(main).Contains("Value: 0", StringComparison.Ordinal), timeout);
            await historyAsync("EditRedoMenuItem");
            await assertCameraValueAsync(expected);

            edit = await openEditorAsync();
            Click(edit, "ClearPropertyButton");
            await WaitAsync(() => Required<TextBlock>(edit, "PropertyEditStatus").Text ==
                "Review edit applied.", timeout);
            await edit.CloseAsync();
            await assertCameraValueAsync(0);
            await WaitAsync(() => ValueText(main).Contains("Value: 0", StringComparison.Ordinal), timeout);
            await historyAsync("EditUndoMenuItem");
            await assertCameraValueAsync(expected);
            await historyAsync("EditUndoMenuItem");
            await assertCameraValueAsync(0);
            await Assert.That(query.Text).IsEqualTo(name);
            await Assert.That(group.SelectedIndex).IsEqualTo((int)ViewerPropertyGroup.Camera);

            async Task<PropertyEditWindow> openEditorAsync()
            {
                await WaitAsync(() => PropertyButtons(main).Single().IsEnabled, timeout);
                PropertyButtons(main).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
                double actual = await session.Scheduler.InvokeAsync(stage =>
                {
                    UsdPrim prim = stage.GetPrim("/Camera");
                    if (isDouble)
                    {
                        return prim.GetDouble(name);
                    }
                    var state = UsdGeomCamera.Wrap(prim).GetState();
                    return name switch
                    {
                        "horizontalApertureOffset" => state.HorizontalApertureOffset,
                        "verticalApertureOffset" => state.VerticalApertureOffset,
                        "focusDistance" => state.FocusDistance,
                        "fStop" => state.FStop,
                        _ => throw new InvalidOperationException($"Unexpected camera field: {name}.")
                    };
                }, timeout);
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
