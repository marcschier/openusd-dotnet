// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace OpenUsd.Viewer;

/// <summary>One toolbar control that the overflow planner may place or defer.</summary>
/// <param name="Name">The control's stable name, matching its <c>x:Name</c>.</param>
/// <param name="Label">The label text the control shows, which is never truncated.</param>
/// <param name="Width">The width the control needs, in logical pixels.</param>
internal readonly record struct ViewerToolbarItem(string Name, string Label, double Width);

/// <summary>The placement one toolbar width produced.</summary>
/// <param name="Visible">The controls shown inline, in toolbar order.</param>
/// <param name="Overflow">The controls moved into the overflow menu, in toolbar order.</param>
/// <param name="UsedWidth">The width the inline controls and the overflow button consume.</param>
internal sealed record ViewerToolbarOverflowPlan(
    IReadOnlyList<ViewerToolbarItem> Visible,
    IReadOnlyList<ViewerToolbarItem> Overflow,
    double UsedWidth)
{
    /// <summary>Gets a value indicating whether an overflow button must be shown.</summary>
    public bool HasOverflow => Overflow.Count != 0;

    /// <summary>
    /// Reports whether the control at one toolbar index is shown inline.
    /// </summary>
    /// <remarks>
    /// The planner defers a contiguous tail, so the visible controls are always the first
    /// <see cref="Visible"/> entries of the authored order. Callers can therefore map an index
    /// straight onto a control without matching names.
    /// </remarks>
    /// <param name="index">The control's index in the authored toolbar order.</param>
    /// <returns><see langword="true"/> when the control is shown inline.</returns>
    public bool IsVisible(int index) => index >= 0 && index < Visible.Count;
}

/// <summary>
/// Decides which toolbar controls fit at a given width and which move into an overflow menu.
/// </summary>
/// <remarks>
/// <para>
/// The viewer already shipped one toolbar bug where controls were clipped rather than moved, and a
/// clipped control is worse than a hidden one: it is visible enough to look available, and its
/// label is cut into something the user cannot read or act on. So the rule here is absolute - a
/// control is either shown at its full width or it is in the overflow menu, and the planner never
/// reports a placement whose total width exceeds the width it was given.
/// </para>
/// <para>
/// The planner is pure so the narrow-width behaviour can be asserted deterministically instead of
/// depending on a layout pass that a headless test cannot run.
/// </para>
/// </remarks>
internal static class ViewerToolbarOverflowPlanner
{
    /// <summary>The width the overflow button itself needs.</summary>
    internal const double OverflowButtonWidth = 40d;

    /// <summary>Plans a toolbar row for one available width.</summary>
    /// <param name="items">The controls, in the order the toolbar presents them.</param>
    /// <param name="availableWidth">The width the toolbar row may consume.</param>
    /// <param name="spacing">The gap between two adjacent controls.</param>
    /// <returns>Which controls are shown inline and which are deferred.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A width is negative or not finite.</exception>
    internal static ViewerToolbarOverflowPlan Plan(
        IReadOnlyList<ViewerToolbarItem> items,
        double availableWidth,
        double spacing = 4d)
    {
        ArgumentNullException.ThrowIfNull(items);
        ThrowIfNotFinitePositive(availableWidth, nameof(availableWidth));
        ThrowIfNotFinitePositive(spacing, nameof(spacing));

        double total = 0d;
        for (int index = 0; index < items.Count; index++)
        {
            ThrowIfNotFinitePositive(items[index].Width, nameof(items));
            total += items[index].Width;
            if (index != 0)
            {
                total += spacing;
            }
        }

        if (items.Count == 0)
        {
            return new ViewerToolbarOverflowPlan([], [], 0d);
        }

        if (total <= availableWidth)
        {
            return new ViewerToolbarOverflowPlan(items, [], total);
        }

        // Everything past this point has to leave room for the overflow button, because at least
        // one control is already known not to fit inline.
        double budget = availableWidth - OverflowButtonWidth;
        var visible = new List<ViewerToolbarItem>(items.Count);
        var overflow = new List<ViewerToolbarItem>(items.Count);
        double used = 0d;
        bool deferring = false;
        for (int index = 0; index < items.Count; index++)
        {
            ViewerToolbarItem item = items[index];
            double additional = visible.Count == 0 ? item.Width : item.Width + spacing;
            if (deferring || used + additional > budget)
            {
                // Once one control is deferred every later control is deferred too, so the
                // overflow menu keeps the toolbar's authored order instead of shuffling controls
                // around as the window resizes.
                deferring = true;
                overflow.Add(item);
                continue;
            }

            visible.Add(item);
            used += additional;
        }

        double usedWidth = used + (visible.Count == 0 ? 0d : spacing) + OverflowButtonWidth;
        if (usedWidth > availableWidth)
        {
            usedWidth = availableWidth;
        }

        return new ViewerToolbarOverflowPlan(visible, overflow, usedWidth);
    }

    private static void ThrowIfNotFinitePositive(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0d)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Toolbar widths must be finite and non-negative.");
        }
    }
}

/// <summary>
/// Builds the menu entries that stand in for the toolbar controls an overflow deferred.
/// </summary>
/// <remarks>
/// A menu entry that merely names a deferred control is not a substitute for the control. Clicking
/// "Speed" in a menu has to change the speed, so a control that holds a choice contributes one
/// checkable entry per choice and operates the deferred control directly; only controls that do one
/// thing when pressed contribute a single entry that forwards to the caller's command handler.
/// </remarks>
internal static class ViewerToolbarOverflowMenu
{
    /// <summary>Creates the menu entry that operates one deferred toolbar control.</summary>
    /// <param name="control">The deferred toolbar control.</param>
    /// <param name="label">The label the entry shows, which is never truncated.</param>
    /// <param name="index">The control's index in the authored toolbar order.</param>
    /// <param name="commandHandler">Invoked when an entry that presses a control is chosen.</param>
    /// <param name="chosen">Invoked after a choice was applied, so the menu can close.</param>
    /// <returns>The menu entry.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="control"/>, <paramref name="label"/>, or <paramref name="commandHandler"/>
    /// is null.
    /// </exception>
    internal static MenuItem CreateItem(
        Control control,
        string label,
        int index,
        EventHandler<RoutedEventArgs> commandHandler,
        Action? chosen = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(commandHandler);
        var item = new MenuItem
        {
            Header = label,
            IsEnabled = control.IsEnabled,
            Tag = index,
        };
        AutomationProperties.SetName(item, label);
        if (control is not SelectingItemsControl selector)
        {
            item.Click += commandHandler;
            return item;
        }

        var choices = new List<MenuItem>(selector.ItemCount);
        for (int choice = 0; choice < selector.ItemCount; choice++)
        {
            int selected = choice;
            string text = DescribeChoice(selector, choice);
            var option = new MenuItem
            {
                Header = text,
                Tag = selected,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = selected == selector.SelectedIndex,
                IsEnabled = selector.IsEnabled,
            };
            AutomationProperties.SetName(option, $"{label} {text}");
            option.Click += (_, args) =>
            {
                args.Handled = true;
                selector.SelectedIndex = selected;
                chosen?.Invoke();
            };
            choices.Add(option);
        }

        item.ItemsSource = choices;
        return item;
    }

    private static string DescribeChoice(SelectingItemsControl selector, int index)
    {
        object? entry = selector.Items[index];
        return entry switch
        {
            ContentControl content => content.Content as string ?? string.Empty,
            null => string.Empty,
            _ => entry.ToString() ?? string.Empty,
        };
    }
}

/// <summary>
/// Writes toolbar control state only when it actually changes.
/// </summary>
/// <remarks>
/// The physics status is repainted about as often as the simulation steps, which is roughly every
/// frame. Writing an unchanged property still invalidates layout, and a toolbar that re-measures a
/// hundred times a second cannot be used: a pointer-driven menu closes under the pointer and the
/// overflow plan is rebuilt for a width that never moved. Reporting whether a write happened lets
/// the caller re-plan only when the layout could have changed.
/// </remarks>
internal static class ViewerToolbarState
{
    /// <summary>Sets a control's content when it differs from what it already shows.</summary>
    /// <param name="control">The control to write.</param>
    /// <param name="content">The content the control should show.</param>
    /// <returns><see langword="true"/> when the control was changed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="control"/> is null.</exception>
    internal static bool SetContent(ContentControl control, string content)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (Equals(control.Content, content))
        {
            return false;
        }

        control.Content = content;
        return true;
    }

    /// <summary>Sets whether a control is enabled when that differs from its current state.</summary>
    /// <param name="control">The control to write.</param>
    /// <param name="enabled">Whether the control accepts input.</param>
    /// <returns><see langword="true"/> when the control was changed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="control"/> is null.</exception>
    internal static bool SetEnabled(Control control, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (control.IsEnabled == enabled)
        {
            return false;
        }

        control.IsEnabled = enabled;
        return true;
    }
}

