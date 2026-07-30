// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[SupportedOSPlatform("linux")]
internal sealed class LinuxGlxStormContextFactory : IStormGlContextFactory
{
    public IStormGlContext Create(int width, int height, SilkColor clearColor)
    {
        try
        {
            return LinuxGlxStormContext.Create(width, height, clearColor);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new InvalidOperationException(
                "The Linux GLX parity shim requires libGL.so.1 and libX11.so.6 with GLX 1.3 entry points.",
                exception);
        }
    }
}

[SupportedOSPlatform("linux")]
internal sealed partial class LinuxGlxStormContext : IStormGlContext
{
    private const int True = 1;
    private const int False = 0;
    private const int None = 0;
    private const int GlxDoubleBuffer = 5;
    private const int GlxRedSize = 8;
    private const int GlxGreenSize = 9;
    private const int GlxBlueSize = 10;
    private const int GlxAlphaSize = 11;
    private const int GlxDepthSize = 12;
    private const int GlxXRenderable = 0x8012;
    private const int GlxDrawableType = 0x8010;
    private const int GlxRenderType = 0x8011;
    private const int GlxWindowBit = 0x00000001;
    private const int GlxPbufferBit = 0x00000004;
    private const int GlxRgbaBit = 0x00000001;
    private const int GlxRgbaType = 0x8014;
    private const int GlxPbufferHeight = 0x8040;
    private const int GlxPbufferWidth = 0x8041;
    private const uint GlColorBufferBit = 0x00004000;
    private const uint GlDepthBufferBit = 0x00000100;
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
    private readonly nint _display;
    private readonly nint _context;
    private readonly nint _pbuffer;
    private readonly GlFunctions _gl;
    private uint _colorTexture;
    private uint _depthRenderbuffer;
    private bool _disposed;

    private LinuxGlxStormContext(
        int width,
        int height,
        nint display,
        nint context,
        nint pbuffer,
        GlFunctions gl,
        uint framebuffer,
        uint colorTexture,
        uint depthRenderbuffer)
    {
        _width = width;
        _height = height;
        _display = display;
        _context = context;
        _pbuffer = pbuffer;
        _gl = gl;
        Framebuffer = framebuffer;
        _colorTexture = colorTexture;
        _depthRenderbuffer = depthRenderbuffer;
    }

    public uint Framebuffer { get; private set; }

    public static LinuxGlxStormContext Create(int width, int height, SilkColor clearColor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        nint display = XOpenDisplay(null);
        if (display == 0)
        {
            throw new InvalidOperationException(
                "XOpenDisplay failed for the GLX parity shim. Ensure DISPLAY points at the Xvfb server.");
        }

        nint configs = 0;
        nint context = 0;
        nint pbuffer = 0;
        try
        {
            int major = 0;
            int minor = 0;
            if (GlXQueryVersion(display, ref major, ref minor) == False)
            {
                throw new InvalidOperationException("glXQueryVersion failed for the GLX parity shim.");
            }

            if (major < 1 || (major == 1 && minor < 3))
            {
                throw new InvalidOperationException(
                    $"The GLX parity shim requires GLX 1.3 for pbuffers; the X server reported {major}.{minor}.");
            }

            int[] attributes =
            [
                GlxXRenderable, True,
                GlxDrawableType, GlxWindowBit | GlxPbufferBit,
                GlxRenderType, GlxRgbaBit,
                GlxRedSize, 8,
                GlxGreenSize, 8,
                GlxBlueSize, 8,
                GlxAlphaSize, 8,
                GlxDepthSize, 24,
                GlxDoubleBuffer, True,
                None
            ];
            int count = 0;
            configs = GlXChooseFBConfig(display, XDefaultScreen(display), attributes, ref count);
            if (configs == 0 || count <= 0)
            {
                throw new InvalidOperationException(
                    "glXChooseFBConfig found no RGBA pbuffer-capable framebuffer configuration.");
            }

            nint config = Marshal.ReadIntPtr(configs);
            context = GlXCreateNewContext(display, config, GlxRgbaType, 0, True);
            if (context == 0)
            {
                throw new InvalidOperationException("glXCreateNewContext failed for the GLX parity shim.");
            }

            int[] pbufferAttributes =
            [
                GlxPbufferWidth, 1,
                GlxPbufferHeight, 1,
                None
            ];
            pbuffer = GlXCreatePbuffer(display, config, pbufferAttributes);
            if (pbuffer == 0)
            {
                throw new InvalidOperationException("glXCreatePbuffer failed for the GLX parity shim.");
            }

            if (GlXMakeContextCurrent(display, pbuffer, pbuffer, context) == False)
            {
                throw new InvalidOperationException("glXMakeContextCurrent failed for the GLX parity shim.");
            }

            GlFunctions gl = GlFunctions.Load();
            uint framebuffer = CreateFramebuffer(gl, width, height, out uint colorTexture, out uint depthRenderbuffer);
            var stormContext = new LinuxGlxStormContext(
                width,
                height,
                display,
                context,
                pbuffer,
                gl,
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
                _ = GlXMakeContextCurrent(display, 0, 0, 0);
            }

            if (pbuffer != 0)
            {
                GlXDestroyPbuffer(display, pbuffer);
            }

            if (context != 0)
            {
                GlXDestroyContext(display, context);
            }

            _ = XCloseDisplay(display);
            throw;
        }
        finally
        {
            if (configs != 0)
            {
                _ = XFree(configs);
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

        _ = GlXMakeContextCurrent(_display, 0, 0, 0);
        GlXDestroyPbuffer(_display, _pbuffer);
        GlXDestroyContext(_display, _context);
        _ = XCloseDisplay(_display);
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
            throw new InvalidOperationException($"The GLX parity framebuffer is incomplete: 0x{status:X}.");
        }

        return framebuffer;
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
            nint address = GlXGetProcAddress(name);
            if (address == 0 && NativeLibrary.TryLoad("libGL.so.1", out nint libGl))
            {
                try
                {
                    _ = NativeLibrary.TryGetExport(libGl, name, out address);
                }
                finally
                {
                    NativeLibrary.Free(libGl);
                }
            }

            if (address == 0)
            {
                throw new InvalidOperationException($"OpenGL function {name} is unavailable in the GLX shim.");
            }

            return Marshal.GetDelegateForFunctionPointer<T>(address);
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

    [LibraryImport("libX11.so.6", EntryPoint = "XOpenDisplay", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint XOpenDisplay(string? displayName);

    [LibraryImport("libX11.so.6", EntryPoint = "XDefaultScreen")]
    private static partial int XDefaultScreen(nint display);

    [LibraryImport("libX11.so.6", EntryPoint = "XCloseDisplay")]
    private static partial int XCloseDisplay(nint display);

    [LibraryImport("libX11.so.6", EntryPoint = "XFree")]
    private static partial int XFree(nint data);

    [LibraryImport("libGL.so.1", EntryPoint = "glXQueryVersion")]
    private static partial int GlXQueryVersion(nint display, ref int major, ref int minor);

    [LibraryImport("libGL.so.1", EntryPoint = "glXChooseFBConfig")]
    private static partial nint GlXChooseFBConfig(
        nint display,
        int screen,
        int[] attributes,
        ref int count);

    [LibraryImport("libGL.so.1", EntryPoint = "glXCreateNewContext")]
    private static partial nint GlXCreateNewContext(
        nint display,
        nint config,
        int renderType,
        nint shareList,
        int direct);

    [LibraryImport("libGL.so.1", EntryPoint = "glXDestroyContext")]
    private static partial void GlXDestroyContext(nint display, nint context);

    [LibraryImport("libGL.so.1", EntryPoint = "glXCreatePbuffer")]
    private static partial nint GlXCreatePbuffer(nint display, nint config, int[] attributes);

    [LibraryImport("libGL.so.1", EntryPoint = "glXDestroyPbuffer")]
    private static partial void GlXDestroyPbuffer(nint display, nint pbuffer);

    [LibraryImport("libGL.so.1", EntryPoint = "glXMakeContextCurrent")]
    private static partial int GlXMakeContextCurrent(nint display, nint draw, nint read, nint context);

    [LibraryImport("libGL.so.1", EntryPoint = "glXGetProcAddressARB", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GlXGetProcAddress(string procedureName);

    [LibraryImport("libGL.so.1", EntryPoint = "glViewport")]
    private static partial void GlViewport(int x, int y, int width, int height);

    [LibraryImport("libGL.so.1", EntryPoint = "glClearColor")]
    private static partial void GlClearColor(float red, float green, float blue, float alpha);

    [LibraryImport("libGL.so.1", EntryPoint = "glClear")]
    private static partial void GlClear(uint mask);

    [LibraryImport("libGL.so.1", EntryPoint = "glFinish")]
    private static partial void GlFinish();

    [LibraryImport("libGL.so.1", EntryPoint = "glReadPixels")]
    private static partial void GlReadPixels(
        int x,
        int y,
        int width,
        int height,
        uint format,
        uint type,
        byte[] pixels);

    [LibraryImport("libGL.so.1", EntryPoint = "glGenTextures")]
    private static partial void GlGenTextures(int count, ref uint textures);

    [LibraryImport("libGL.so.1", EntryPoint = "glBindTexture")]
    private static partial void GlBindTexture(uint target, uint texture);

    [LibraryImport("libGL.so.1", EntryPoint = "glTexParameteri")]
    private static partial void GlTexParameteri(uint target, uint name, int parameter);

    [LibraryImport("libGL.so.1", EntryPoint = "glTexImage2D")]
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

    [LibraryImport("libGL.so.1", EntryPoint = "glDeleteTextures")]
    private static partial void GlDeleteTextures(int count, ref uint textures);
}
