// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.App;

/// <summary>
/// Thin desktop entry point. All shell behaviour lives in the embeddable
/// <c>OpenUsd.Viewer</c> library so hosts can run the same viewport on a stage
/// scheduler they own themselves.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args) => ViewerEntryPoint.Run(args);
}
