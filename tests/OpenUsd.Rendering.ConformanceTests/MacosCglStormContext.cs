// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[SupportedOSPlatform("macos")]
internal sealed class MacosCglStormContextFactory : IStormGlContextFactory
{
    public IStormGlContext Create(int width, int height, SilkColor clearColor)
    {
        try
        {
            return MacosCglStormContext.Create(width, height, clearColor);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new InvalidOperationException(
                "The macOS CGL parity shim requires the system OpenGL framework with CGL entry points.",
                exception);
        }
    }
}

[SupportedOSPlatform("macos")]
internal sealed partial class MacosCglStormContext : IStormGlContext
{
    private const string OpenGlFramework = "/System/Library/Frameworks/OpenGL.framework/OpenGL";
    private const int CglSuccess = 0;
    private const int CglPfaColorSize = 8;
    private const int CglPfaAlphaSize = 11;
    private const int CglPfaDepthSize = 12;
    private const int CglPfaStencilSize = 13;
    private const int CglPfaAccelerated = 73;
    private const int CglPfaAllowOfflineRenderers = 96;
    private const int CglPfaOpenGlProfile = 99;
    private const int CglOglPVersion32Core = 0x3200;
    private const uint GlColorBufferBit = 0x00004000;
    private const uint GlDepthBufferBit = 0x00000100;
    private const uint GlVendor = 0x1F00;
    private const uint GlRenderer = 0x1F01;
    private const uint GlVersion = 0x1F02;
    private const uint GlTexture2D = 0x0DE1;
    private const uint GlRgba = 0x1908;
    private const uint GlUnsignedByte = 0x1401;
    private const uint GlLinear = 0x2601;
    private const uint GlTextureMinFilter = 0x2801;
    private const uint GlTextureMagFilter = 0x2800;
    private const uint GlFramebuffer = 0x8D40;
    private const uint GlRenderbuffer = 0x8D41;
    private const uint GlColorAttachment0 = 0x8CE0;
    private const uint GlDepthAttachment = 0x8D00;
    private const uint GlDepthComponent24 = 0x81A6;
    private const uint GlFramebufferComplete = 0x8CD5;

    private readonly int _width;
    private readonly int _height;
    private readonly nint _context;
    private readonly GlFunctions _gl;
    private uint _colorTexture;
    private uint _depthRenderbuffer;
    private bool _disposed;

    private MacosCglStormContext(
        int width,
        int height,
        nint context,
        GlFunctions gl,
        StormOpenGlEvidence openGlEvidence,
        uint framebuffer,
        uint colorTexture,
        uint depthRenderbuffer)
    {
        _width = width;
        _height = height;
        _context = context;
        _gl = gl;
        OpenGlEvidence = openGlEvidence;
        Framebuffer = framebuffer;
        _colorTexture = colorTexture;
        _depthRenderbuffer = depthRenderbuffer;
    }

    public uint Framebuffer { get; private set; }

    public StormOpenGlEvidence OpenGlEvidence { get; }

    public static MacosCglStormContext Create(int width, int height, SilkColor clearColor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        int[] attributes =
        [
            CglPfaOpenGlProfile, CglOglPVersion32Core,
            CglPfaAccelerated,
            CglPfaAllowOfflineRenderers,
            CglPfaColorSize, 32,
            CglPfaAlphaSize, 8,
            CglPfaDepthSize, 24,
            CglPfaStencilSize, 8,
            0
        ];
        nint pixelFormat = 0;
        nint context = 0;
        Check(CGLChoosePixelFormat(attributes, ref pixelFormat, out int pixelFormatCount), "CGLChoosePixelFormat");
        if (pixelFormat == 0 || pixelFormatCount <= 0)
        {
            throw new InvalidOperationException("CGLChoosePixelFormat found no accelerated OpenGL pixel format.");
        }

        try
        {
            Check(CGLCreateContext(pixelFormat, 0, ref context), "CGLCreateContext");
            Check(CGLSetCurrentContext(context), "CGLSetCurrentContext");
            GlFunctions gl = GlFunctions.Load();
            StormOpenGlEvidence evidence = CaptureOpenGlEvidence(context);
            uint framebuffer = CreateFramebuffer(gl, width, height, out uint colorTexture, out uint depthRenderbuffer);
            var stormContext = new MacosCglStormContext(
                width,
                height,
                context,
                gl,
                evidence,
                framebuffer,
                colorTexture,
                depthRenderbuffer);
            stormContext.Clear(clearColor);
            return stormContext;
        }
        catch
        {
            if (context != 0)
            {
                _ = CGLSetCurrentContext(0);
                _ = CGLDestroyContext(context);
            }

            throw;
        }
        finally
        {
            if (pixelFormat != 0)
            {
                _ = CGLDestroyPixelFormat(pixelFormat);
            }
        }
    }

    public void Clear(SilkColor clearColor)
    {
        ThrowIfDisposed();
        _gl.BindFramebuffer(GlFramebuffer, Framebuffer);
        GlViewport(0, 0, _width, _height);
        GlClearColor(clearColor.Red, clearColor.Green, clearColor.Blue, clearColor.Alpha);
        GlClear(GlColorBufferBit | GlDepthBufferBit);
    }

    public void Finish()
    {
        ThrowIfDisposed();
        GlFinish();
    }

    public ParityImage ReadTopDownRgba()
    {
        ThrowIfDisposed();
        byte[] bottomUp = new byte[_width * _height * ParityImage.BytesPerPixel];
        _gl.BindFramebuffer(GlFramebuffer, Framebuffer);
        GlReadPixels(0, 0, _width, _height, GlRgba, GlUnsignedByte, bottomUp);
        return ParityImage.FromBottomUpRgba(_width, _height, bottomUp);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Framebuffer != 0)
        {
            uint framebuffer = Framebuffer;
            _gl.DeleteFramebuffers(1, ref framebuffer);
            Framebuffer = 0;
        }

        if (_depthRenderbuffer != 0)
        {
            uint renderbuffer = _depthRenderbuffer;
            _gl.DeleteRenderbuffers(1, ref renderbuffer);
            _depthRenderbuffer = 0;
        }

        if (_colorTexture != 0)
        {
            uint texture = _colorTexture;
            GlDeleteTextures(1, ref texture);
            _colorTexture = 0;
        }

        _ = CGLSetCurrentContext(0);
        _ = CGLDestroyContext(_context);
    }

    private static uint CreateFramebuffer(
        GlFunctions gl,
        int width,
        int height,
        out uint colorTexture,
        out uint depthRenderbuffer)
    {
        uint framebuffer = 0;
        colorTexture = 0;
        depthRenderbuffer = 0;
        gl.GenFramebuffers(1, ref framebuffer);
        gl.BindFramebuffer(GlFramebuffer, framebuffer);
        GlGenTextures(1, ref colorTexture);
        GlBindTexture(GlTexture2D, colorTexture);
        GlTexParameteri(GlTexture2D, GlTextureMinFilter, (int)GlLinear);
        GlTexParameteri(GlTexture2D, GlTextureMagFilter, (int)GlLinear);
        GlTexImage2D(GlTexture2D, 0, (int)GlRgba, width, height, 0, GlRgba, GlUnsignedByte, 0);
        gl.FramebufferTexture2D(GlFramebuffer, GlColorAttachment0, GlTexture2D, colorTexture, 0);
        gl.GenRenderbuffers(1, ref depthRenderbuffer);
        gl.BindRenderbuffer(GlRenderbuffer, depthRenderbuffer);
        gl.RenderbufferStorage(GlRenderbuffer, GlDepthComponent24, width, height);
        gl.FramebufferRenderbuffer(GlFramebuffer, GlDepthAttachment, GlRenderbuffer, depthRenderbuffer);
        uint status = gl.CheckFramebufferStatus(GlFramebuffer);
        if (status != GlFramebufferComplete)
        {
            throw new InvalidOperationException($"The CGL parity framebuffer is incomplete: 0x{status:X}.");
        }

        return framebuffer;
    }

    private static StormOpenGlEvidence CaptureOpenGlEvidence(nint context) =>
        new(
            OpenGlFramework,
            string.Empty,
            GlString(GlVendor),
            GlString(GlRenderer),
            GlString(GlVersion),
            string.Empty,
            $"0x{context:X}");

    private static string GlString(uint name)
    {
        nint value = GlGetString(name);
        return value == 0 ? string.Empty : Marshal.PtrToStringAnsi(value) ?? string.Empty;
    }

    private static void Check(int error, string operation)
    {
        if (error != CglSuccess)
        {
            throw new InvalidOperationException($"{operation} failed with CGL error {error}.");
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class GlFunctions
    {
        internal readonly GlGenFramebuffers GenFramebuffers;
        internal readonly GlBindFramebuffer BindFramebuffer;
        internal readonly GlDeleteFramebuffers DeleteFramebuffers;
        internal readonly GlFramebufferTexture2D FramebufferTexture2D;
        internal readonly GlCheckFramebufferStatus CheckFramebufferStatus;
        internal readonly GlGenRenderbuffers GenRenderbuffers;
        internal readonly GlBindRenderbuffer BindRenderbuffer;
        internal readonly GlRenderbufferStorage RenderbufferStorage;
        internal readonly GlFramebufferRenderbuffer FramebufferRenderbuffer;
        internal readonly GlDeleteRenderbuffers DeleteRenderbuffers;

        private GlFunctions(
            GlGenFramebuffers genFramebuffers,
            GlBindFramebuffer bindFramebuffer,
            GlDeleteFramebuffers deleteFramebuffers,
            GlFramebufferTexture2D framebufferTexture2D,
            GlCheckFramebufferStatus checkFramebufferStatus,
            GlGenRenderbuffers genRenderbuffers,
            GlBindRenderbuffer bindRenderbuffer,
            GlRenderbufferStorage renderbufferStorage,
            GlFramebufferRenderbuffer framebufferRenderbuffer,
            GlDeleteRenderbuffers deleteRenderbuffers)
        {
            GenFramebuffers = genFramebuffers;
            BindFramebuffer = bindFramebuffer;
            DeleteFramebuffers = deleteFramebuffers;
            FramebufferTexture2D = framebufferTexture2D;
            CheckFramebufferStatus = checkFramebufferStatus;
            GenRenderbuffers = genRenderbuffers;
            BindRenderbuffer = bindRenderbuffer;
            RenderbufferStorage = renderbufferStorage;
            FramebufferRenderbuffer = framebufferRenderbuffer;
            DeleteRenderbuffers = deleteRenderbuffers;
        }

        internal static GlFunctions Load() =>
            new(
                Load<GlGenFramebuffers>("glGenFramebuffers"),
                Load<GlBindFramebuffer>("glBindFramebuffer"),
                Load<GlDeleteFramebuffers>("glDeleteFramebuffers"),
                Load<GlFramebufferTexture2D>("glFramebufferTexture2D"),
                Load<GlCheckFramebufferStatus>("glCheckFramebufferStatus"),
                Load<GlGenRenderbuffers>("glGenRenderbuffers"),
                Load<GlBindRenderbuffer>("glBindRenderbuffer"),
                Load<GlRenderbufferStorage>("glRenderbufferStorage"),
                Load<GlFramebufferRenderbuffer>("glFramebufferRenderbuffer"),
                Load<GlDeleteRenderbuffers>("glDeleteRenderbuffers"));

        private static T Load<T>(string name)
            where T : Delegate
        {
            if (!NativeLibrary.TryLoad(OpenGlFramework, out nint openGl))
            {
                throw new InvalidOperationException("The OpenGL framework could not be loaded for the CGL shim.");
            }

            try
            {
                if (!NativeLibrary.TryGetExport(openGl, name, out nint address) || address == 0)
                {
                    throw new InvalidOperationException($"OpenGL function {name} is unavailable in the CGL shim.");
                }

                return Marshal.GetDelegateForFunctionPointer<T>(address);
            }
            finally
            {
                NativeLibrary.Free(openGl);
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlGenFramebuffers(int count, ref uint framebuffers);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlBindFramebuffer(uint target, uint framebuffer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlDeleteFramebuffers(int count, ref uint framebuffers);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlFramebufferTexture2D(
        uint target,
        uint attachment,
        uint textureTarget,
        uint texture,
        int level);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint GlCheckFramebufferStatus(uint target);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlGenRenderbuffers(int count, ref uint renderbuffers);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlBindRenderbuffer(uint target, uint renderbuffer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlRenderbufferStorage(uint target, uint format, int width, int height);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlFramebufferRenderbuffer(
        uint target,
        uint attachment,
        uint renderbufferTarget,
        uint renderbuffer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlDeleteRenderbuffers(int count, ref uint renderbuffers);

    [LibraryImport(OpenGlFramework, EntryPoint = "CGLChoosePixelFormat")]
    private static partial int CGLChoosePixelFormat(int[] attributes, ref nint pixelFormat, out int count);

    [LibraryImport(OpenGlFramework, EntryPoint = "CGLDestroyPixelFormat")]
    private static partial int CGLDestroyPixelFormat(nint pixelFormat);

    [LibraryImport(OpenGlFramework, EntryPoint = "CGLCreateContext")]
    private static partial int CGLCreateContext(nint pixelFormat, nint shareContext, ref nint context);

    [LibraryImport(OpenGlFramework, EntryPoint = "CGLDestroyContext")]
    private static partial int CGLDestroyContext(nint context);

    [LibraryImport(OpenGlFramework, EntryPoint = "CGLSetCurrentContext")]
    private static partial int CGLSetCurrentContext(nint context);

    [LibraryImport(OpenGlFramework, EntryPoint = "glViewport")]
    private static partial void GlViewport(int x, int y, int width, int height);

    [LibraryImport(OpenGlFramework, EntryPoint = "glClearColor")]
    private static partial void GlClearColor(float red, float green, float blue, float alpha);

    [LibraryImport(OpenGlFramework, EntryPoint = "glClear")]
    private static partial void GlClear(uint mask);

    [LibraryImport(OpenGlFramework, EntryPoint = "glFinish")]
    private static partial void GlFinish();

    [LibraryImport(OpenGlFramework, EntryPoint = "glReadPixels")]
    private static partial void GlReadPixels(
        int x,
        int y,
        int width,
        int height,
        uint format,
        uint type,
        byte[] pixels);

    [LibraryImport(OpenGlFramework, EntryPoint = "glGetString")]
    private static partial nint GlGetString(uint name);

    [LibraryImport(OpenGlFramework, EntryPoint = "glGenTextures")]
    private static partial void GlGenTextures(int count, ref uint textures);

    [LibraryImport(OpenGlFramework, EntryPoint = "glBindTexture")]
    private static partial void GlBindTexture(uint target, uint texture);

    [LibraryImport(OpenGlFramework, EntryPoint = "glTexParameteri")]
    private static partial void GlTexParameteri(uint target, uint name, int parameter);

    [LibraryImport(OpenGlFramework, EntryPoint = "glTexImage2D")]
    private static partial void GlTexImage2D(
        uint target,
        int level,
        int internalFormat,
        int width,
        int height,
        int border,
        uint format,
        uint type,
        nint pixels);

    [LibraryImport(OpenGlFramework, EntryPoint = "glDeleteTextures")]
    private static partial void GlDeleteTextures(int count, ref uint textures);
}
