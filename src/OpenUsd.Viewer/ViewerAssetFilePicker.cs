// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace OpenUsd.Viewer;

internal interface IViewerAssetFilePicker
{
    Task<string?> OpenAssetAsync(Window owner, CancellationToken cancellationToken);
}

internal sealed class ViewerAssetFilePicker : IViewerAssetFilePicker
{
    internal static ViewerAssetFilePicker Default { get; } = new();

    public async Task<string?> OpenAssetAsync(Window owner, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose review asset",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.All]
        }).WaitAsync(cancellationToken);
        if (files.Count == 0)
        {
            return null;
        }
        return files[0].TryGetLocalPath() ??
            throw new NotSupportedException(
                "Choose a local filesystem asset, or enter its authored path explicitly.");
    }
}
