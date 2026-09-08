// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using OpenUsd.Interop;
using OpenUsd.Rendering;

[assembly: SupportedOSPlatform("windows")]

internal static unsafe partial class Program
{
    private static int checks;

    private static byte[] LiteralHalf()
    {
        ReadOnlySpan<ushort> samples =
        [
            0x3000, 0x3800, 0x4000, 0x3c00, 0xb800, 0x4400, 0x5400, 0x3400,
            0x3400, 0x8000, 0x4a00, 0x3a00, 0x3e00, 0xc000, 0x3800, 0x0000,
            0x5000, 0x3800, 0x3400, 0x3800, 0xc400, 0x4800, 0x3000, 0x3c00,
            0x2c00, 0x4c00, 0xb800, 0x8000, 0x4000, 0x4400, 0x4800, 0x3a00,
        ];
        byte[] result = new byte[64];
        for (int i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(i * 2, 2), samples[i]);
        }
        return result;
    }

    private static void Require(bool condition, string message)
    {
        checks++;
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            checks++;
            return;
        }
        throw new InvalidDataException($"Expected {typeof(T).Name}.");
    }

    private static FileStream Open(string path, FileOptions options = FileOptions.None) =>
        new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, options);

    private static long Encode(
        FileStream stream, byte[] pixels, long quota,
        Rgba16FloatRowOrder rows = Rgba16FloatRowOrder.TopDown,
        CancellationToken token = default) =>
        ExrRgba16FloatWriter.Write(stream, 4, 2, pixels, rows, quota, token);

    private static void Verify(string path, byte[] expected, bool bottomUp = false)
    {
        byte[] decoded = ExrScanlineOracle.Read(File.ReadAllBytes(path), 4, 2);
        if (bottomUp)
        {
            Require(decoded.AsSpan(0, 32).SequenceEqual(expected.AsSpan(32, 32)) &&
                decoded.AsSpan(32, 32).SequenceEqual(expected.AsSpan(0, 32)), "Bottom-up half bits changed.");
        }
        else
        {
            Require(decoded.AsSpan().SequenceEqual(expected), "Top-down half bits changed.");
        }
    }

    private static void ModuleIdentity(string name, string expectedSha256)
    {
        nint module = GetModuleHandleW(name);
        Require(module != 0, "Expected package native module was not loaded.");
        Span<char> pathBuffer = stackalloc char[1024];
        uint length;
        fixed (char* pointer = pathBuffer)
        {
            length = GetModuleFileNameW(module, pointer, (uint)pathBuffer.Length);
        }
        Require(length > 0 && length < (uint)pathBuffer.Length, "Module path is unavailable.");
        string path = Path.GetFullPath(new string(pathBuffer[..(int)length]));
        string app = Path.GetFullPath(AppContext.BaseDirectory);
        Require(path.StartsWith(app, StringComparison.OrdinalIgnoreCase), "Native dependency escaped the package app.");
        using FileStream file = File.OpenRead(path);
        string hash = Convert.ToHexString(SHA256.HashData(file));
        Require(hash == expectedSha256, "Native package/install identity mismatch.");
        Console.WriteLine($"PUBLIC_EXR_MODULE={name}; SHA256={hash}");
    }

    private static void CancelAfterOutputBegins(string root, bool disposeOwner)
    {
        string path = Path.Combine(root, disposeOwner ? "dispose-cancel.partial" : "cancel.partial");
        byte[] pixels = new byte[4 * 8192 * 8];
        using FileStream stream = Open(path);
        nint handle = stream.SafeFileHandle.DangerousGetHandle();
        using CancellationTokenSource cancellation = new();
        using ManualResetEventSlim watching = new();
        Task observer = Task.Run(() =>
        {
            FileInfo file = new(path);
            watching.Set();
            for (int i = 0; i < 10000; i++)
            {
                file.Refresh();
                if (file.Length > 0)
                {
                    if (disposeOwner)
                    {
                        stream.Dispose();
                    }
                    cancellation.Cancel();
                    return;
                }
                Thread.Sleep(1);
            }
            throw new TimeoutException("Native output did not begin.");
        });
        watching.Wait();
        Throws<OperationCanceledException>(() =>
            ExrRgba16FloatWriter.Write(stream, 4, 8192, pixels, 1_048_576, cancellation.Token));
        observer.GetAwaiter().GetResult();
        long length = new FileInfo(path).Length;
        Require(length is > 0 and <= 1_048_576, "Cancellation lost the partial-output bound.");
        if (disposeOwner)
        {
            Require(GetHandleInformation(handle, out _) == 0, "Owner disposal did not drain the borrowed handle.");
        }
        else
        {
            Require(!stream.SafeFileHandle.IsClosed, "Cancellation closed the caller handle.");
            stream.SetLength(0);
            stream.Position = 0;
            byte[] literal = LiteralHalf();
            long written = Encode(stream, literal, 4096);
            Require(stream.Position == written && stream.Length == written, "Same-stream reset/retry failed.");
            stream.Dispose();
            Verify(path, literal);
        }
    }

    private static void InvalidInputsAndStreams(string root, byte[] pixels)
    {
        using (FileStream stream = Open(Path.Combine(root, "invalid.partial")))
        {
            byte[] nonfinite = (byte[])pixels.Clone();
            BinaryPrimitives.WriteUInt16LittleEndian(nonfinite.AsSpan(62), 0x7e01);
            Throws<ArgumentException>(() => Encode(stream, nonfinite, 4096));
            Throws<ArgumentException>(() =>
                ExrRgba16FloatWriter.Write(stream, 4, 2, pixels.AsSpan(0, 63), 4096));
            Throws<ArgumentOutOfRangeException>(() =>
                ExrRgba16FloatWriter.Write(stream, int.MaxValue, 2, pixels, 4096));
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Throws<OperationCanceledException>(() => Encode(stream, pixels, 4096, token: cancellation.Token));
            Require(stream.Length == 0 && stream.Position == 0, "Preflight refusal mutated output.");
        }
        using (FileStream stream = Open(Path.Combine(root, "async.partial"), FileOptions.Asynchronous))
        {
            Throws<ArgumentException>(() => Encode(stream, pixels, 4096));
            Require(stream.Length == 0, "Asynchronous output profile was written.");
        }
        using (FileStream stream = Open(Path.Combine(root, "nonempty.partial")))
        {
            stream.WriteByte(81);
            Throws<ArgumentException>(() => Encode(stream, pixels, 4096));
            Require(stream.Length == 1 && stream.Position == 1, "Nonempty caller output changed.");
        }
        using (FileStream stream = Open(Path.Combine(root, "disposed.partial")))
        {
            stream.Dispose();
            Throws<ArgumentException>(() => Encode(stream, pixels, 4096));
        }
    }

    private static void DiskJobOutput(string root, byte[] pixels)
    {
        StageRenderState state = StageRenderState.Create(new StageIdentity("package-exr-source"))
            .WithViewport(new ViewportDimensions(4, 2));
        var source = new HdrJobSource(pixels);
        var request = new RenderDiskJobRequest(Path.Combine(root, "sequence"), [state], false, true,
            RenderHdrColorFormat.Exr);
        RenderDiskJobResult result = RenderDiskJob.Execute(request, source);
        RenderDiskFrameResult frame = result.Frames[0];
        Require(frame.HdrColorFormat == RenderHdrColorFormat.Exr &&
            frame.HdrColorFileName == "frame-000000.hdr.exr", "EXR job descriptor mismatch.");
        string path = Path.Combine(result.OutputDirectory, frame.HdrColorFileName!);
        Verify(path, pixels);
        Require(frame.HdrColorBytes == new FileInfo(path).Length &&
            frame.HdrColorSha256 == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
            "EXR job hash/extent mismatch.");
        Require(result.TotalBytes == Directory.GetFiles(result.OutputDirectory).Sum(
            static file => new FileInfo(file).Length), "Combined EXR job quota accounting mismatch.");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(result.OutputDirectory, "manifest.json")));
        Require(manifest.RootElement.GetProperty("frames")[0].GetProperty("hdrColor")
            .GetProperty("format").GetString() == "openexr-rgba16float", "EXR job manifest format mismatch.");
        var shortRequest = new RenderDiskJobRequest(Path.Combine(root, "short-sequence"), [state], false, true,
            RenderHdrColorFormat.Exr, new RenderDiskJobLimits(
                maximumFrameBytes: frame.Bytes + frame.HdrColorBytes - 1));
        Throws<RenderOutputQuotaExceededException>(() => RenderDiskJob.Execute(shortRequest, source));
        Require(!Directory.Exists(shortRequest.OutputDirectory) &&
            !Directory.EnumerateDirectories(root, ".openusd-render-*").Any(),
            "A failed EXR job left published or staging output.");
        Console.WriteLine("PUBLIC_EXR_DISK_JOB=passed");
    }

    private sealed class HdrJobSource(byte[] pixels) : IRenderJobFrameSource
    {
        public RenderJobImage Render(StageRenderState state, CancellationToken cancellationToken) =>
            new(4, 2, new byte[32], Rgba8RowOrder.TopDown) { HdrColor = new RenderJobHdrColor(4, 2, pixels) };
    }

    private static int Main(string[] args)
    {
        try
        {
            Require(args.Length == 2, "Expected data and SDK SHA256 identities.");
            Require(ExrRgba16FloatWriter.IsSupported && !RuntimeFeature.IsDynamicCodeSupported &&
                !RuntimeFeature.IsDynamicCodeCompiled, "Actual supported-host NativeAOT execution is required.");
            uint abi = OpenUsdNativeRuntime.AbiVersion;
            ulong capabilities = OpenUsdNativeRuntime.Capabilities;
            Require(abi >= 24 && abi == OpenUsdNativeContract.AbiVersion &&
                capabilities == OpenUsdNativeContract.RequiredCapabilities &&
                (capabilities & (1UL << 32)) != 0, "Guarded Data ABI24/EXR capability mismatch.");
            Console.WriteLine($"PUBLIC_EXR_CONTRACT=matched; ABI={abi}; capabilities={capabilities}");
            Console.WriteLine("PUBLIC_EXR_NATIVE_AOT=true");
            string root = Directory.CreateDirectory("exr-output").FullName;
            byte[] pixels = LiteralHalf();
            long exact = 0;
            string top = Path.Combine(root, "top.exr");
            foreach (bool bottomUp in new[] { false, true })
            {
                string path = bottomUp ? Path.Combine(root, "bottom.exr") : top;
                using (FileStream stream = Open(path))
                {
                    long written = Encode(stream, pixels, 4096,
                        bottomUp ? Rgba16FloatRowOrder.BottomUp : Rgba16FloatRowOrder.TopDown);
                    Require(written == stream.Length && written == stream.Position &&
                        !stream.SafeFileHandle.IsClosed, "Success extent/cursor/ownership mismatch.");
                    if (!bottomUp)
                    {
                        exact = written;
                    }
                }
                Verify(path, pixels, bottomUp);
            }
            byte[] valid = File.ReadAllBytes(top);
            byte[] corrupt = (byte[])valid.Clone();
            corrupt[0] ^= 1;
            Throws<InvalidDataException>(() => ExrScanlineOracle.Read(corrupt, 4, 2));
            Throws<InvalidDataException>(() => ExrScanlineOracle.Read(valid[..^1], 4, 2));
            byte[] wrongExpected = (byte[])pixels.Clone();
            wrongExpected[0] ^= 1;
            Require(!ExrScanlineOracle.Read(valid, 4, 2).AsSpan().SequenceEqual(wrongExpected),
                "Independent oracle did not distinguish changed input bits.");
            foreach (long quota in new[] { 0L, 64L, exact - 1, exact, exact + 1 })
            {
                string path = Path.Combine(root, $"quota-{quota}.exr");
                using (FileStream stream = Open(path))
                {
                    if (quota >= exact)
                    {
                        Require(Encode(stream, pixels, quota) == exact, "Exact-boundary success failed.");
                    }
                    else
                    {
                        Throws<RenderOutputQuotaExceededException>(() => Encode(stream, pixels, quota));
                    }
                    Require(stream.Length <= quota && !stream.SafeFileHandle.IsClosed, "Quota or ownership violated.");
                    if (quota <= 64)
                    {
                        Require(stream.Length == 0, "Too-small quota mutated output.");
                    }
                    if (quota == exact - 1)
                    {
                        Require(stream.Length > 0, "Expected a bounded late-quota partial file.");
                        stream.SetLength(0);
                        stream.Position = 0;
                        Require(Encode(stream, pixels, exact) == exact, "Quota reset/retry failed.");
                    }
                }
                if (quota >= exact - 1)
                {
                    Verify(path, pixels);
                }
            }
            InvalidInputsAndStreams(root, pixels);
            CancelAfterOutputBegins(root, disposeOwner: false);
            CancelAfterOutputBegins(root, disposeOwner: true);
            Require(pixels.AsSpan().SequenceEqual(LiteralHalf()), "Encoder changed caller input.");
            using (var png = new MemoryStream())
            {
                _ = PngRgba8Writer.Write(png, 1, 1, [1, 2, 3, 4], 1024);
                Require(png.ToArray().AsSpan(0, 8).SequenceEqual<byte>([137, 80, 78, 71, 13, 10, 26, 10]),
                    "Existing Core/PNG behavior regressed.");
            }
            ModuleIdentity("openusd_dotnet.dll", args[0]);
            ModuleIdentity("usd_ms.dll", args[1]);
            DiskJobOutput(root, pixels);
            Console.WriteLine($"PUBLIC_EXR_INDEPENDENT_HALF_BITS=passed; rows=both; exactBytes={exact}");
            Console.WriteLine("PUBLIC_EXR_QUOTA_CANCEL_OWNERSHIP=passed");
            Console.WriteLine("PUBLIC_EXR_NATIVE_HEAP=not-quota-certified");
            Console.WriteLine($"PUBLIC_EXR_PACKAGE_ONLY=passed; checks={checks}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"PUBLIC_EXR_PACKAGE_ONLY=failed; {exception}");
            return 1;
        }
    }

    [LibraryImport("kernel32", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string name);

    [LibraryImport("kernel32")]
    private static partial uint GetModuleFileNameW(nint module, char* buffer, uint length);

    [LibraryImport("kernel32", SetLastError = true)]
    private static partial int GetHandleInformation(nint handle, out uint flags);
}
