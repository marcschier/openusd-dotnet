// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using OpenUsd.Interop;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed partial class SilkNativePageCopyRecoveryTests
{
    private const string Scene = """
        #usda 1.0
        def Mesh "Removed" {
            uniform token subdivisionScheme = "none"
            point3f[] points = [(0,0,0), (1,0,0), (0,1,0)]
            int[] faceVertexCounts = [3]
            int[] faceVertexIndices = [0,1,2]
        }
        """;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FailedNativeCopyRetainsRetirementUntilNextSuccessfulCopy(bool bounded)
    {
        using var fixture = new Fixture(Scene);
        using var session = new NativeSession(fixture.Plugins, fixture.Path, bounded);
        using OpenUsdSilkPage first = session.Sync();
        var retained = new SilkSceneState();
        retained.Apply(first);
        await Assert.That(retained.MeshesByPath.ContainsKey(("/Removed", 0))).IsTrue();
        session.RemovePrim("/Removed");
        NativeCopy.CopyFailure = AllocationFailure();
        await Assert.That(() => session.Sync()).Throws<OutOfMemoryException>();
        await Assert.That(NativeCopy.RejectedRemovals).IsEqualTo(1);
        await Assert.That(NativeCopy.Acknowledgements).IsEqualTo(1);
        await Assert.That(NativeCopy.Releases).IsEqualTo(2);
        string rejectedHash = session.LastNativeHash;
        NativeCopy.ClearFailures();
        using OpenUsdSilkPage retry = session.Sync();
        retained.Apply(retry);
        await Assert.That(retained.MeshesByPath.ContainsKey(("/Removed", 0))).IsFalse();
        await Assert.That(retained.Meshes.Count).IsEqualTo(0);
        await Assert.That(session.LastNativeHash).IsEqualTo(rejectedHash);
        await Assert.That(retry.Revision).IsEqualTo(first.Revision + 2);
        using OpenUsdSilkPage quiet = session.Sync();
        await Assert.That(quiet.CommandCount).IsEqualTo(1u);
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo(Scene);
    }

    [Test]
    [Arguments("copy")]
    [Arguments("usage")]
    [Arguments("truncated")]
    [Arguments("acknowledgement")]
    [Arguments("limit")]
    public async Task RepeatedCopyStageFailuresPreserveMeshMaterialAndEnvironmentRetirements(string mode)
    {
        string scene = Scene + """

            def Material "Material" {
                token outputs:surface.connect = </Material/Surface.outputs:surface>
                def Shader "Surface" {
                    uniform token info:id = "UsdPreviewSurface"
                    color3f inputs:diffuseColor = (0.2, 0.3, 0.4)
                    token outputs:surface
                }
            }
            def DomeLight "Environment" {
                asset inputs:texture:file = @deliberately-absent.exr@
                token inputs:texture:format = "latlong"
            }
            def DistantLight "Light" (prepend apiSchemas = ["ShadowAPI"]) {
                float inputs:intensity = 10
                bool inputs:shadow:enable = true
            }
            """;
        using var fixture = new Fixture(scene);
        using var session = new NativeSession(fixture.Plugins, fixture.Path);
        using OpenUsdSilkPage initial = session.Sync();
        var retained = new SilkSceneState();
        retained.Apply(initial);
        await Assert.That(retained.Materials.ContainsKey("/Material")).IsTrue();
        await Assert.That(retained.Environments.ContainsKey("/Environment")).IsTrue();
        foreach (string path in new[] { "/Removed", "/Material", "/Environment", "/Light" })
        {
            session.RemovePrim(path);
        }
        string? firstRejectedHash = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            NativeCopy.ConfigureFailure(mode);
            int? copyLimit = mode == "limit" ? 1 : null;
            if (mode == "copy")
            {
                await Assert.That(() => session.Sync(copyLimit: copyLimit)).Throws<OutOfMemoryException>();
            }
            else if (mode is "truncated" or "limit")
            {
                await Assert.That(() => session.Sync(copyLimit: copyLimit)).Throws<OpenUsdSilkException>();
            }
            else
            {
                await Assert.That(() => session.Sync(copyLimit: copyLimit)).Throws<InvalidOperationException>();
            }
            firstRejectedHash ??= session.LastNativeHash;
            await Assert.That(session.LastNativeHash).IsEqualTo(firstRejectedHash);
            await Assert.That(retained.Revision).IsEqualTo(initial.Revision);
            await Assert.That(retained.Meshes.Count).IsEqualTo(1);
            await Assert.That(retained.Materials.ContainsKey("/Material")).IsTrue();
            await Assert.That(retained.Environments.ContainsKey("/Environment")).IsTrue();
        }
        NativeCopy.ClearFailures();
        using OpenUsdSilkPage retry = session.Sync();
        await Assert.That(session.LastNativeHash).IsEqualTo(firstRejectedHash);
        var types = new HashSet<SilkCommandType>();
        using (SilkCommandEnumerator commands = retry.GetEnumerator())
        {
            while (commands.MoveNext())
            {
                types.Add(commands.Current.Type);
            }
        }
        await Assert.That(types.Contains(SilkCommandType.MeshRemove)).IsTrue();
        await Assert.That(types.Contains(SilkCommandType.MaterialRemove)).IsTrue();
        await Assert.That(types.Contains(SilkCommandType.EnvironmentRemove)).IsTrue();
        retained.Apply(retry);
        await Assert.That(retained.Meshes.Count).IsEqualTo(0);
        await Assert.That(retained.Materials.Count).IsEqualTo(0);
        await Assert.That(retained.Environments.Count).IsEqualTo(0);
        using OpenUsdSilkPage quiet = session.Sync();
        await Assert.That(quiet.CommandCount).IsEqualTo(1u);
        await Assert.That(Native.LivePages()).IsEqualTo((nuint)0);
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo(scene);
    }

    [Test]
    public async Task RequestAfterCopyFailureUsesItsCurrentFrameWithoutLosingThePendingDeletion()
    {
        using var fixture = new Fixture(Scene);
        using var session = new NativeSession(fixture.Plugins, fixture.Path);
        using OpenUsdSilkPage initial = session.Sync();
        var retained = new SilkSceneState();
        retained.Apply(initial);
        session.RemovePrim("/Removed");
        NativeCopy.CopyFailure = AllocationFailure();
        await Assert.That(() => session.Sync()).Throws<OutOfMemoryException>();
        NativeCopy.ClearFailures();
        using OpenUsdSilkPage current = session.Sync(width: 33, height: 27);
        retained.Apply(current);
        await Assert.That(retained.Frame.Width).IsEqualTo(33);
        await Assert.That(retained.Frame.Height).IsEqualTo(27);
        await Assert.That(retained.Meshes.Count).IsEqualTo(0);
    }

    [Test]
    public async Task StageEditDuringRejectedCopyMergesWithEarlierUnacknowledgedRetirement()
    {
        string scene = Scene + "\n" + Scene.Replace("#usda 1.0", string.Empty, StringComparison.Ordinal)
            .Replace("\"Removed\"", "\"Later\"", StringComparison.Ordinal);
        using var fixture = new Fixture(scene);
        using var session = new NativeSession(fixture.Plugins, fixture.Path);
        using OpenUsdSilkPage initial = session.Sync();
        var retained = new SilkSceneState();
        retained.Apply(initial);
        session.RemovePrim("/Removed");
        NativeCopy.BeforeCopy = () => session.RemovePrim("/Later");
        NativeCopy.CopyFailure = AllocationFailure();
        await Assert.That(() => session.Sync()).Throws<OutOfMemoryException>();
        await Assert.That(retained.Meshes.Count).IsEqualTo(2);
        NativeCopy.ClearFailures();
        using OpenUsdSilkPage retry = session.Sync();
        retained.Apply(retry);
        await Assert.That(retained.Meshes.Count).IsEqualTo(0);
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo(scene);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root;
        internal string Path { get; }
        internal string Plugins { get; }

        internal Fixture(string scene)
        {
            Plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH") ?? string.Empty;
            if (Plugins.Length == 0)
            {
                if (Environment.GetEnvironmentVariable("OPENUSD_MESH_PREPARATION_REQUIRED") == "1")
                {
                    throw new InvalidOperationException("Native copy recovery requires matching plugin inputs.");
                }
                Skip.Test("Set OPENUSD_TEST_PLUGIN_PATH to the qualified native runtime.");
                throw new InvalidOperationException("Skip.Test returned unexpectedly.");
            }
            NativeCopy.Reset();
            _root = Directory.CreateTempSubdirectory("silk-copy-recovery-").FullName;
            Path = System.IO.Path.Combine(_root, "scene.usda");
            File.WriteAllText(Path, scene);
        }

        public void Dispose()
        {
            NativeCopy.Reset();
            Directory.Delete(_root, recursive: true);
        }
    }

    [SuppressMessage("Usage", "CA2201", Justification = "Deterministic copy failure without exhausting host memory.")]
    private static OutOfMemoryException AllocationFailure() => new("Injected native-to-managed copy refusal.");

    private sealed unsafe class NativeSession : IDisposable
    {
        private nint _session;
        private nint _stage;
        private readonly bool _bounded;
        internal string LastNativeHash { get; private set; } = string.Empty;

        internal NativeSession(string plugins, string path, bool bounded = true)
        {
            _bounded = bounded;
            Span<byte> message = stackalloc byte[4096];
            fixed (byte* pointer = message)
            {
                var error = new Error(pointer, (nuint)message.Length);
                Check(Native.OpenStage(path, out _stage, ref error), message);
                try
                {
                    Check(Native.Create(plugins, _stage, out _session, ref error), message);
                    Check(Native.EnableAcknowledgement(_session, ref error), message);
                    if (bounded)
                    {
                        var limits = new OpenUsdSilkRuntime.NativeMeshPreparationPageLimits(24, 1_000_000, 1_000_000);
                        Check(Native.SetLimits(_session, &limits, ref error), message);
                    }
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }
        }

        internal OpenUsdSilkPage Sync(int width = 16, int height = 16, int? copyLimit = null)
        {
            var view = new OpenUsdSilkRuntime.NativePageView
            {
                StructSize = (uint)sizeof(OpenUsdSilkRuntime.NativePageView)
            };
            var camera = new NativeRenderCamera(CameraState.Default);
            Span<byte> message = stackalloc byte[4096];
            fixed (byte* pointer = message)
            {
                var error = new Error(pointer, (nuint)message.Length);
                Check(Native.Sync(_session, width, height, 0, &camera, out nint page, &view, ref error), message);
                LastNativeHash = Convert.ToHexString(SHA256.HashData(
                    new ReadOnlySpan<byte>((void*)view.Data, checked((int)view.DataSize))));
                return OpenUsdSilkRuntime.CopyPreparedPage<NativeCopy>(
                    page, in view, default, _bounded ? 1_000_000ul : null,
                    copyLimit ?? (_bounded ? 1_000_000 : null), readPageUsage: _bounded);
            }
        }

        internal void RemovePrim(string path)
        {
            Span<byte> message = stackalloc byte[4096];
            fixed (byte* pointer = message)
            {
                var error = new Error(pointer, (nuint)message.Length);
                Check(Native.RemovePrim(_stage, path, ref error), message);
            }
        }

        public void Dispose()
        {
            Span<byte> message = stackalloc byte[4096];
            fixed (byte* pointer = message)
            {
                var error = new Error(pointer, (nuint)message.Length);
                if (_session != 0)
                {
                    Check(Native.Destroy(_session, ref error), message);
                    _session = 0;
                }
                if (_stage != 0)
                {
                    Native.ReleaseStage(_stage);
                    _stage = 0;
                }
            }
        }
    }

    private readonly unsafe struct NativeCopy : OpenUsdSilkRuntime.IPreparedPageCopy
    {
        internal static Exception? CopyFailure { get; set; }
        internal static Action? BeforeCopy { get; set; }
        private static Exception? UsageFailure { get; set; }
        private static Exception? AcknowledgementFailure { get; set; }
        private static bool Truncate { get; set; }
        internal static int RejectedRemovals { get; set; }
        internal static int Acknowledgements { get; set; }
        internal static int Releases { get; set; }

        internal static void ClearFailures()
        {
            CopyFailure = null;
            BeforeCopy = null;
            UsageFailure = null;
            AcknowledgementFailure = null;
            Truncate = false;
        }

        internal static void Reset()
        {
            ClearFailures();
            RejectedRemovals = 0;
            Acknowledgements = 0;
            Releases = 0;
        }

        internal static void ConfigureFailure(string mode)
        {
            ClearFailures();
            CopyFailure = mode == "copy" ? AllocationFailure() : null;
            UsageFailure = mode == "usage" ? new InvalidOperationException("Injected usage read failure.") : null;
            AcknowledgementFailure = mode == "acknowledgement"
                ? new InvalidOperationException("Injected acknowledgement failure.") : null;
            Truncate = mode == "truncated";
        }

        public static OpenUsdSilkRuntime.NativeMeshPreparationUsage ReadUsage(nint page)
        {
            if (UsageFailure is { } failure)
            {
                throw failure;
            }
            var usage = new OpenUsdSilkRuntime.NativeMeshPreparationUsage { StructSize = 32 };
            Span<byte> message = stackalloc byte[4096];
            fixed (byte* pointer = message)
            {
                var error = new Error(pointer, (nuint)message.Length);
                Check(Native.Usage(page, &usage, ref error), message);
            }
            return usage;
        }

        public static byte[] Copy(in OpenUsdSilkRuntime.NativePageView view)
        {
            Action? beforeCopy = BeforeCopy;
            BeforeCopy = null;
            beforeCopy?.Invoke();
            if (CopyFailure is { } error)
            {
                using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(
                    new ReadOnlySpan<byte>((void*)view.Data, checked((int)view.DataSize)),
                    view.CommandCount, view.AbiVersion);
                while (commands.MoveNext())
                {
                    if (commands.Current.Type == SilkCommandType.MeshRemove)
                    {
                        RejectedRemovals++;
                    }
                }
                throw error;
            }
            return new ReadOnlySpan<byte>((void*)view.Data,
                checked((int)view.DataSize) - (Truncate ? 1 : 0)).ToArray();
        }

        public static void Acknowledge(nint page)
        {
            Acknowledgements++;
            if (AcknowledgementFailure is { } failure)
            {
                throw failure;
            }
            Span<byte> message = stackalloc byte[4096];
            fixed (byte* pointer = message)
            {
                var error = new Error(pointer, (nuint)message.Length);
                Check(Native.Acknowledge(page, ref error), message);
            }
        }

        public static void Release(nint page)
        {
            Releases++;
            Native.Release(page);
        }
    }

    private static void Check(OpenUsdNativeStatus status, ReadOnlySpan<byte> message)
    {
        if (status != OpenUsdNativeStatus.Ok)
        {
            int end = message.IndexOf((byte)0);
            throw new OpenUsdSilkException(status, Encoding.UTF8.GetString(end < 0 ? message : message[..end]));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct Error(byte* data, nuint capacity)
    {
        internal byte* Data = data;
        internal nuint Capacity = capacity;
        internal nuint Required;
    }

    private static unsafe partial class Native
    {
        [LibraryImport("openusd_hdsilk", EntryPoint = "openusd_silk_diagnostic_get_live_page_count")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial nuint LivePages();

        [LibraryImport("openusd_dotnet", EntryPoint = "openusd_stage_open",
            StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus OpenStage(string path, out nint stage, ref Error error);

        [LibraryImport("openusd_dotnet", EntryPoint = "openusd_stage_remove_prim",
            StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus RemovePrim(nint stage, string path, ref Error error);

        [LibraryImport("openusd_dotnet", EntryPoint = "openusd_stage_release")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial void ReleaseStage(nint stage);

        [LibraryImport("openusd_hdsilk", EntryPoint = "openusd_silk_session_create_from_stage",
            StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus Create(
            string plugins, nint stage, out nint session, ref Error error);

        [LibraryImport("openusd_hdsilk", EntryPoint = "openusd_silk_session_set_preparation_limits")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus SetLimits(
            nint session, OpenUsdSilkRuntime.NativeMeshPreparationPageLimits* limits, ref Error error);

        [LibraryImport("openusd_hdsilk", EntryPoint = "openusd_silk_session_sync")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus Sync(
            nint session, int width, int height, double time, NativeRenderCamera* camera, out nint page,
            OpenUsdSilkRuntime.NativePageView* view, ref Error error);

        [LibraryImport("openusd_hdsilk", EntryPoint = "openusd_silk_page_get_preparation_usage")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus Usage(
            nint page, OpenUsdSilkRuntime.NativeMeshPreparationUsage* usage, ref Error error);

        [LibraryImport("openusd_hdsilk", EntryPoint = "openusd_silk_session_enable_page_acknowledgement")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus EnableAcknowledgement(nint session, ref Error error);

        [LibraryImport("openusd_hdsilk", EntryPoint = "openusd_silk_page_acknowledge")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus Acknowledge(nint page, ref Error error);

        [LibraryImport("openusd_hdsilk", EntryPoint = "openusd_silk_page_release")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial void Release(nint page);

        [LibraryImport("openusd_hdsilk", EntryPoint = "openusd_silk_session_destroy")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus Destroy(nint session, ref Error error);
    }
}
