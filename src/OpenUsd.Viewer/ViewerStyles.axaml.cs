// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace OpenUsd.Viewer;

internal sealed partial class ViewerStyles : Styles
{
    public ViewerStyles()
    {
        AvaloniaXamlLoader.Load(this);
        ViewerPaletteResources.Populate(Resources);
    }
}
