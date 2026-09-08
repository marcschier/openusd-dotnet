// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OpenUsd.Viewer;

internal sealed partial class ViewerContrastResources : ResourceDictionary
{
    public ViewerContrastResources()
    {
        AvaloniaXamlLoader.Load(this);
        ViewerPaletteResources.Populate(this);
    }
}
