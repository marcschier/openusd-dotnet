// Copyright (c) marcschier. Licensed under the MIT License.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OpenUsd.Interop;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

internal sealed class ViewerPortableReviewFixture : IDisposable
{
    internal const string SourceText =
        """
        #usda 1.0
        (
            defaultPrim = "Body"
            customLayerData = { string marker = "original source" }
            subLayers = [@sublayer.usda@]
        )
        def Xform "Body"
        {
            custom double review:value = 7
            asset review:texture = @texture.png@
            def Mesh "Quad" ( prepend apiSchemas = ["MaterialBindingAPI"] )
            {
                int[] faceVertexCounts = [4]
                int[] faceVertexIndices = [0, 1, 2, 3]
                point3f[] points = [(-1, -1, 0), (1, -1, 0), (1, 1, 0), (-1, 1, 0)]
                texCoord2f[] primvars:st = [(0, 0), (1, 0), (1, 1), (0, 1)] ( interpolation = "vertex" )
                rel material:binding = </Material>
            }
        }
        def Material "Material"
        {
            token outputs:surface.connect = </Material/Preview.outputs:surface>
            def Shader "Preview"
            {
                uniform token info:id = "UsdPreviewSurface"
                color3f inputs:diffuseColor.connect = </Material/Texture.outputs:rgb>
                token outputs:surface
            }
            def Shader "Texture"
            {
                uniform token info:id = "UsdUVTexture"
                asset inputs:file = @texture.png@
                float2 inputs:st.connect = </Material/Coordinates.outputs:result>
                float3 outputs:rgb
            }
            def Shader "Coordinates"
            {
                uniform token info:id = "UsdPrimvarReader_float2"
                token inputs:varname = "st"
                float2 outputs:result
            }
        }
        """;
    internal const string SublayerText = "#usda 1.0\ndef Xform \"FromSublayer\" {}\n";
    internal static byte[] AssetBytes
    {
        get
        {
            using var stream = new MemoryStream();
            _ = PngRgba8Writer.Write(stream, 1, 1, [255, 255, 255, 255], 1024, CancellationToken.None);
            return stream.ToArray();
        }
    }

    private ViewerPortableReviewFixture(string root) => Root = root;

    internal string Root { get; }
    internal string SourcePath => Path.Combine(Root, "source.usda");
    internal string DestinationPath => Path.Combine(Root, "review.urd");

    internal static async Task<ViewerPortableReviewFixture> CreateAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Verified portable review opening currently requires Windows.");
        }
        string plugins = ViewerNativeTestStages.RequirePluginPathOrSkip();
        OpenUsdNativeRuntime.RegisterPlugins(plugins);
        string parent = Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT") ?? Path.GetTempPath();
        if (!Path.IsPathFullyQualified(parent))
        {
            throw new InvalidOperationException("The portable review test root must be an absolute physical path.");
        }
        var fixture = new ViewerPortableReviewFixture(Path.Combine(parent, $"viewer-portable-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(fixture.Root);
        await File.WriteAllTextAsync(fixture.SourcePath, SourceText);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "sublayer.usda"), SublayerText);
        await File.WriteAllBytesAsync(Path.Combine(fixture.Root, "texture.png"), AssetBytes);
        return fixture;
    }

    internal async Task AssertOriginalsUnchangedAsync()
    {
        await Assert.That(await File.ReadAllTextAsync(SourcePath)).IsEqualTo(SourceText);
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(Root, "sublayer.usda"))).IsEqualTo(SublayerText);
        await Assert.That((await File.ReadAllBytesAsync(Path.Combine(Root, "texture.png"))).SequenceEqual(AssetBytes))
            .IsTrue();
    }

    [SupportedOSPlatform("windows")]
    internal static void CreateHardLink(string path, string existing)
    {
        if (!CreateHardLinkW(path, existing, 0))
        {
            throw new IOException("Could not create the hard-link fixture.",
                new Win32Exception(Marshal.GetLastPInvokeError()));
        }
    }

    [SupportedOSPlatform("windows")]
    internal static async Task CreateJunctionAsync(string path, string target)
    {
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(
            $"New-Item -ItemType Junction -Path '{path.Replace("'", "''", StringComparison.Ordinal)}' " +
            $"-Target '{target.Replace("'", "''", StringComparison.Ordinal)}' -ErrorAction Stop | Out-Null");
        using Process process = Process.Start(start) ??
            throw new IOException("Could not start the Windows junction fixture.");
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (process.ExitCode != 0)
            {
                throw new IOException(await process.StandardError.ReadToEndAsync());
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string path, string existing, nint security);
}
