// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace OpenUsd.Viewer;

/// <summary>
/// Collects and validates one physics bake request before any layer is touched.
/// </summary>
/// <remarks>
/// Baking is the one physics action that writes to a file the user keeps, so it is deliberately an
/// explicit dialog rather than a toolbar toggle. The dialog validates continuously and refuses to
/// close until the request is one the transport can actually run, because a bake that fails after
/// the dialog has gone leaves the user with nothing to correct.
/// </remarks>
internal sealed partial class PhysicsBakeWindow : Window
{
    /// <summary>Creates an empty bake dialog. Used by the Avalonia designer.</summary>
    public PhysicsBakeWindow()
        : this(0d, 1d)
    {
    }

    /// <summary>Creates a bake dialog seeded from the authored timeline.</summary>
    /// <param name="startTimeCode">The authored start time code.</param>
    /// <param name="endTimeCode">The authored end time code.</param>
    /// <param name="suggestedDestination">The destination path to offer, or an empty string.</param>
    public PhysicsBakeWindow(
        double startTimeCode,
        double endTimeCode,
        string suggestedDestination = "")
    {
        InitializeComponent();
        BakeDestinationInput.Text = suggestedDestination;
        BakeStartInput.Text = Format(startTimeCode);
        BakeEndInput.Text = Format(endTimeCode);
        BakeStrideInput.Text = "1";
        BakeCancelButton.Click += (_, _) => Close(null);
        BakeConfirmButton.Click += (_, _) => Confirm();
        BakeBrowseButton.Click += async (_, _) => await BrowseAsync();
        BakeDestinationInput.TextChanged += (_, _) => Revalidate();
        BakeStartInput.TextChanged += (_, _) => Revalidate();
        BakeEndInput.TextChanged += (_, _) => Revalidate();
        BakeStrideInput.TextChanged += (_, _) => Revalidate();
        Revalidate();
    }

    /// <summary>Builds a request from the current dialog state, if it is complete.</summary>
    /// <param name="request">Receives the request.</param>
    /// <returns><see langword="true"/> when every field parsed.</returns>
    internal bool TryBuildRequest(out ViewerPhysicsBakeRequest request)
    {
        request = null!;
        if (!TryParse(BakeStartInput.Text, out double start) ||
            !TryParse(BakeEndInput.Text, out double end) ||
            !TryParse(BakeStrideInput.Text, out double stride))
        {
            return false;
        }

        request = new ViewerPhysicsBakeRequest(
            BakeDestinationInput.Text ?? string.Empty,
            start,
            end,
            stride,
            SelectedPolicy(),
            BakeSaveCheckBox.IsChecked == true);
        return true;
    }

    private static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool TryParse(string? text, out double value) =>
        double.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);

    private ViewerPhysicsBakePolicy SelectedPolicy() =>
        ((BakePolicySelector.SelectedItem as ComboBoxItem)?.Tag as string) switch
        {
            "Overwrite" => ViewerPhysicsBakePolicy.Overwrite,
            "Skip" => ViewerPhysicsBakePolicy.Skip,
            _ => ViewerPhysicsBakePolicy.Reject,
        };

    private void Confirm()
    {
        if (!TryBuildRequest(out ViewerPhysicsBakeRequest request))
        {
            Revalidate();
            return;
        }

        ViewerPhysicsBakeValidation validation = ViewerPhysicsBakeValidator.Validate(request);
        if (!validation.IsValid)
        {
            BakeValidationText.Text = validation.Message;
            return;
        }

        Close(request);
    }

    private void Revalidate()
    {
        if (!TryBuildRequest(out ViewerPhysicsBakeRequest request))
        {
            BakeValidationText.Text =
                "Enter the range and stride as invariant numbers, for example 0, 48, and 1.";
            BakeConfirmButton.IsEnabled = false;
            return;
        }

        ViewerPhysicsBakeValidation validation = ViewerPhysicsBakeValidator.Validate(request);
        BakeValidationText.Text = validation.Message;
        BakeConfirmButton.IsEnabled = validation.IsValid;
    }

    private async Task BrowseAsync()
    {
        IStorageProvider? storage = StorageProvider;
        if (storage is null)
        {
            return;
        }

        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Choose the bake destination layer",
            DefaultExtension = "usda",
            ShowOverwritePrompt = true,
            FileTypeChoices =
            [
                new FilePickerFileType("USD layers")
                {
                    Patterns = ["*.usd", "*.usda", "*.usdc"],
                },
            ],
        });
        if (file?.TryGetLocalPath() is { Length: > 0 } path)
        {
            BakeDestinationInput.Text = path;
        }
    }
}
