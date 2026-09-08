// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class NativeSearchPathTests
{
    [Test]
    public async Task ConfiguredTestRuntimeCannotBeOverriddenByAnOlderRepositoryInstall()
    {
        string root = Directory.CreateTempSubdirectory("openusd-viewer-native-").FullName;
        try
        {
            string configured = Path.Combine(root, "configured");
            Directory.CreateDirectory(Path.Combine(configured, "bin"));
            Directory.CreateDirectory(Path.Combine(configured, "lib"));
            Directory.CreateDirectory(Path.Combine(root, "native", "install", "shim", "win-x64", "bin"));

            string[] directories = NativeSearchPath.ResolveDirectories(root, configured);

            await Assert.That(directories.Length).IsEqualTo(2);
            await Assert.That(directories[0]).IsEqualTo(Path.Combine(configured, "bin"));
            await Assert.That(directories[1]).IsEqualTo(Path.Combine(configured, "lib"));
            await Assert.That(() => NativeSearchPath.ResolveDirectories(root, "relative-runtime"))
                .Throws<ArgumentException>();
            await Assert.That(() => NativeSearchPath.ResolveDirectories(root, Path.Combine(root, "missing")))
                .Throws<DirectoryNotFoundException>();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
