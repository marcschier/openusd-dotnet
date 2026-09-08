// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace OpenUsd.Viewer;

internal interface IViewerFrameFilePicker
{
    Task<string?> SaveFrameAsync(Window owner, CancellationToken cancellationToken);
}

internal sealed class ViewerFrameFilePicker : IViewerFrameFilePicker
{
    internal static ViewerFrameFilePicker Default { get; } = new();

    public async Task<string?> SaveFrameAsync(Window owner, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Capture Frame",
            SuggestedFileName = "openusd-viewer-frame.png",
            DefaultExtension = "png",
            FileTypeChoices =
            [
                new FilePickerFileType("PNG image") { Patterns = ["*.png"] },
                new FilePickerFileType("Bitmap image") { Patterns = ["*.bmp"] }
            ]
        }).WaitAsync(cancellationToken);
        return file is null ? null : file.TryGetLocalPath() ??
            throw new NotSupportedException("Choose a local filesystem destination for the captured frame.");
    }
}
