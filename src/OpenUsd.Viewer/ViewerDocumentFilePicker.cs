// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace OpenUsd.Viewer;

internal interface IViewerDocumentFilePicker
{
    Task<string?> SaveReviewAsync(Window owner, string suggestedPath);
    Task<string?> OpenReviewAsync(Window owner);
}

internal sealed class ViewerDocumentFilePicker : IViewerDocumentFilePicker
{
    internal static ViewerDocumentFilePicker Default { get; } = new();

    public async Task<string?> SaveReviewAsync(Window owner, string suggestedPath)
    {
        IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save review document",
            SuggestedFileName = Path.GetFileName(suggestedPath),
            DefaultExtension = "urd",
            FileTypeChoices = [new FilePickerFileType("Source-linked review document") { Patterns = ["*.urd"] }]
        });
        return file is null ? null : file.TryGetLocalPath() ??
            throw new NotSupportedException("Choose a local filesystem destination for the review document.");
    }

    public async Task<string?> OpenReviewAsync(Window owner)
    {
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open source-linked review document",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Source-linked review document") { Patterns = ["*.urd"] }]
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath() ??
            throw new NotSupportedException("Choose a local review document.");
    }
}
