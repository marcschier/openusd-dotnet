// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OpenUsd.Editing;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerPropertyPagingNativeTests
{
    private static async Task ExerciseComponentPropertiesAsync(
        MainWindow main, ViewerStageSession session, TreeView tree, TextBox query, ComboBox group,
        CancellationToken timeout)
    {
        foreach ((string path, string name, string type, string[] labels, double[] components) in new[]
        {
            ("/Camera", "clippingRange", "float2", new[] { "X", "Y" }, new[] { -0.5, 250d }),
            ("/Light", "inputs:color", "color3f", new[] { "R", "G", "B" }, new[] { -0.5, 2d, 65504d }),
            ("/Components", "review:pair", "float2", new[] { "X", "Y" }, new[] { -1.25, 4d }),
            ("/Components", "review:direction", "vector3d", new[] { "X", "Y", "Z" },
                new[] { -1.0000000000000002, 0.12500000000000003, 1e100 }),
            ("/Components", "review:tint", "color4f", new[] { "R", "G", "B", "A" },
                new[] { -0.25, 4d, 0.5, 2d }),
            ("/Components", "review:rotation", "quatf", new[] { "Real", "X", "Y", "Z" },
                new[] { -2d, 0.25, -0.5, 4d }),
            ("/Components", "review:matrix", "matrix4d",
                new[] { "M00", "M01", "M02", "M03", "M10", "M11", "M12", "M13",
                    "M20", "M21", "M22", "M23", "M30", "M31", "M32", "M33" },
                new[] { 1.0000000000000002, 0.12500000000000003, -3d, 4d,
                    5d, -6d, 1e100, 8d, 9d, 10d, -11d, 12d, -4.25, 2.5, -0.75, 16d })
        })
        {
            group.SelectedIndex = (int)ViewerPropertyGroup.All;
            tree.SelectedItem = tree.Items.OfType<TreeViewItem>().Single(
                item => item.Tag is ViewerHierarchyTreeNode node && node.Entry.Path == path);
            query.Text = name;
            string identity = $"edit.property:{path}.{name}";
            await WaitAsync(() => PropertyButtons(main).Any(button =>
                AutomationProperties.GetAutomationId(button) == identity && button.IsEnabled), timeout,
                $"{path}.{name} edit action");
            PropertyButtons(main).Single(button => AutomationProperties.GetAutomationId(button) == identity)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => main.OwnedWindows.OfType<PropertyEditWindow>().Any(), timeout);
            PropertyEditWindow edit = main.OwnedWindows.OfType<PropertyEditWindow>().Single();
            await WaitAsync(() => Required<Button>(edit, "ClearPropertyButton").IsEnabled, timeout);
            StackPanel inputs = Required<StackPanel>(edit, "PropertyComponentValues");
            await Assert.That(inputs.IsVisible && inputs.IsEnabled).IsTrue();
            await Assert.That(Required<TextBox>(edit, "PropertyValue").IsVisible).IsFalse();
            await Assert.That(Required<CheckBox>(edit, "PropertyBooleanValue").IsVisible).IsFalse();
            var boxes = new List<TextBox>();
            foreach (string label in labels)
            {
                TextBox box = inputs.GetVisualDescendants().OfType<TextBox>()
                    .Single(box => box.Name == $"PropertyComponent{label}");
                await Assert.That(AutomationProperties.GetAutomationId(box))
                    .IsEqualTo($"edit.propertyComponent:{label}");
                await Assert.That(AutomationProperties.GetName(box)).IsEqualTo($"Review component {label}");
                await Assert.That(box.Text).IsEqualTo(string.Empty);
                boxes.Add(box);
            }
            await Assert.That(boxes.Count).IsEqualTo(inputs.GetVisualDescendants().OfType<TextBox>().Count());
            if (type == "matrix4d")
            {
                Grid matrix = inputs.GetVisualDescendants().OfType<Grid>().Single(grid =>
                    AutomationProperties.GetAutomationId(grid) == "edit.propertyMatrix");
                await Assert.That(matrix.RowDefinitions.Count).IsEqualTo(4);
                await Assert.That(matrix.ColumnDefinitions.Count).IsEqualTo(4);
                for (int index = 0; index < boxes.Count; index++)
                {
                    Grid cell = boxes[index].Parent as Grid
                        ?? throw new InvalidOperationException("Each matrix input must have a labelled cell.");
                    await Assert.That(ReferenceEquals(cell.Parent, matrix)).IsTrue();
                    await Assert.That(Grid.GetRow(cell)).IsEqualTo(index / 4);
                    await Assert.That(Grid.GetColumn(cell)).IsEqualTo(index % 4);
                }
            }
            await Assert.That(Required<Button>(edit, "SetPropertyButton").IsEnabled).IsFalse();
            await Assert.That((await readReviewComponentAsync()).Value.Kind).IsEqualTo(UsdLayerEditValueKind.Absent);
            boxes[0].Text = "1";
            await Assert.That(Required<Button>(edit, "SetPropertyButton").IsEnabled).IsFalse();
            Click(edit, "SetPropertyButton");
            await Assert.That(Required<TextBlock>(edit, "PropertyEditStatus").Text)
                .Contains("Enter exactly 1 components.");
            await Assert.That((await readReviewComponentAsync()).Value.Kind).IsEqualTo(UsdLayerEditValueKind.Absent);
            for (int index = 0; index < boxes.Count; index++)
            {
                boxes[index].Text = components[index].ToString("R", CultureInfo.InvariantCulture);
            }
            await Assert.That(Required<Button>(edit, "SetPropertyButton").IsEnabled).IsTrue();
            await Assert.That((await readReviewComponentAsync()).Value.Kind).IsEqualTo(UsdLayerEditValueKind.Absent);
            foreach (string invalid in new[] { "NaN", "Infinity", "1, 2", string.Empty })
            {
                boxes[0].Text = invalid;
                await Assert.That(Required<Button>(edit, "SetPropertyButton").IsEnabled).IsFalse();
                Click(edit, "SetPropertyButton");
                await Assert.That(Required<TextBlock>(edit, "PropertyEditStatus").Text)
                    .DoesNotContain("Review edit applied.");
                await Assert.That((await readReviewComponentAsync()).Value.Kind)
                    .IsEqualTo(UsdLayerEditValueKind.Absent);
            }
            boxes[0].Text = type is "vector3d" or "matrix4d" ? "1e309" : "1e100";
            await Assert.That(Required<Button>(edit, "SetPropertyButton").IsEnabled).IsFalse();
            boxes[0].Text = components[0].ToString("R", CultureInfo.InvariantCulture);
            Click(edit, "SetPropertyButton");
            await waitForComponentOperationAsync();
            await assertComponentsAsync(await readReviewComponentAsync());
            boxes[0].Text = "99";
            Click(edit, "RefreshPropertyButton");
            await WaitAsync(() => Required<Button>(edit, "RefreshPropertyButton").IsEnabled, timeout);
            await Assert.That(boxes[0].Text).IsEqualTo(components[0].ToString("R", CultureInfo.InvariantCulture));
            Click(edit, "BlockPropertyButton");
            await waitForComponentOperationAsync();
            await Assert.That((await readReviewComponentAsync()).Value.Kind).IsEqualTo(UsdLayerEditValueKind.Block);
            await Assert.That(boxes.All(static box => box.Text == string.Empty)).IsTrue();
            await Assert.That(Required<Button>(edit, "SetPropertyButton").IsEnabled).IsFalse();
            Click(edit, "ClearPropertyButton");
            await waitForComponentOperationAsync();
            await Assert.That((await readReviewComponentAsync()).Value.Kind).IsEqualTo(UsdLayerEditValueKind.Absent);
            Required<ComboBox>(edit, "PropertyFieldSelector").SelectedIndex = 1;
            await WaitAsync(() => Required<Button>(edit, "ClearPropertyButton").IsEnabled, timeout);
            await Assert.That(boxes.All(static box => box.Text == string.Empty)).IsTrue();
            for (int index = 0; index < boxes.Count; index++)
            {
                boxes[index].Text = components[index].ToString("R", CultureInfo.InvariantCulture);
            }
            Click(edit, "SetPropertyButton");
            await waitForComponentOperationAsync();
            await assertComponentsAsync(await readReviewComponentAsync(UsdLayerEditField.TimeSample));
            await Assert.That((await readReviewComponentAsync()).Value.Kind).IsEqualTo(UsdLayerEditValueKind.Absent);
            await edit.CloseAsync();
            foreach (string action in new[] { "EditUndoMenuItem", "EditRedoMenuItem", "EditUndoMenuItem" })
            {
                await WaitAsync(() => Required<MenuItem>(main, action).IsEnabled, timeout);
                Required<MenuItem>(main, action).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                string inverse = action == "EditUndoMenuItem" ? "EditRedoMenuItem" : "EditUndoMenuItem";
                await WaitAsync(() => Required<MenuItem>(main, inverse).IsEnabled, timeout);
                UsdLayerAuthoredOpinion opinion = await readReviewComponentAsync(UsdLayerEditField.TimeSample);
                if (action == "EditRedoMenuItem")
                {
                    await assertComponentsAsync(opinion);
                }
                else
                {
                    await Assert.That(opinion.Value.Kind).IsEqualTo(UsdLayerEditValueKind.Absent);
                }
            }

            async Task<UsdLayerAuthoredOpinion> readReviewComponentAsync(
                UsdLayerEditField field = UsdLayerEditField.Default) =>
                await session.Scheduler.InvokeAsync(stage =>
                {
                    using UsdLayer layer = stage.GetUserReviewLayer();
                    return layer.CaptureAuthored([new UsdLayerEditAddress($"{path}.{name}", field,
                        field == UsdLayerEditField.Default ? 0 : 5)]).Opinions[0];
                }, timeout);

            async Task waitForComponentOperationAsync() =>
                await WaitAsync(() => Required<Button>(edit, "ClearPropertyButton").IsEnabled &&
                    Required<TextBlock>(edit, "PropertyEditStatus").Text == "Review edit applied.", timeout);

            async Task assertComponentsAsync(UsdLayerAuthoredOpinion opinion)
            {
                await Assert.That(opinion.TypeName).IsEqualTo(type);
                if (components.Length == 2)
                {
                    await Assert.That(opinion.Value.AsVec2f())
                        .IsEqualTo(new UsdVec2f((float)components[0], (float)components[1]));
                }
                else if (type == "vector3d")
                {
                    await Assert.That(opinion.Value.AsVec3d())
                        .IsEqualTo(new UsdVec3d(components[0], components[1], components[2]));
                }
                else if (components.Length == 3)
                {
                    await Assert.That(opinion.Value.AsVec3f())
                        .IsEqualTo(new UsdVec3f((float)components[0], (float)components[1], (float)components[2]));
                }
                else if (type == "quatf")
                {
                    await Assert.That(opinion.Value.AsQuatf()).IsEqualTo(
                        new UsdQuatf((float)components[0], (float)components[1],
                            (float)components[2], (float)components[3]));
                }
                else if (type == "matrix4d")
                {
                    UsdMatrix4d matrix = opinion.Value.AsMatrix4d();
                    for (int row = 0; row < 4; row++)
                    {
                        for (int column = 0; column < 4; column++)
                        {
                            await Assert.That(matrix[row, column]).IsEqualTo(components[row * 4 + column]);
                        }
                    }
                }
                else
                {
                    await Assert.That(opinion.Value.AsVec4f()).IsEqualTo(
                        new UsdVec4f((float)components[0], (float)components[1],
                            (float)components[2], (float)components[3]));
                }
            }
        }
    }
}
