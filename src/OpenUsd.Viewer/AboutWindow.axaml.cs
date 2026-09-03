// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;

namespace OpenUsd.Viewer;

/// <summary>
/// Shows basic Viewer version and Omniverse compatibility-profile guidance, opened from Help.
/// </summary>
/// <remarks>
/// Deliberately static and version-agnostic: it points at
/// <c>docs/omniverse-profile.md</c> rather than repeating specific Kit/OpenUSD baseline claims
/// here, so this dialog cannot drift out of step with the checked, versioned profile that is
/// the actual source of truth for what is supported.
/// </remarks>
internal sealed partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        string version = typeof(AboutWindow).Assembly.GetName().Version?.ToString() ?? "unknown";
        VersionText.Text = $"Version: {version}";
        CloseButton.Click += (_, _) => Close();
    }
}
