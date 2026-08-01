// Copyright (c) marcschier. Licensed under the MIT License.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[SupportedOSPlatform("windows")]
internal sealed class WindowsWglStormContextFactory : IStormGlContextFactory
{
    public IStormGlContext Create(int width, int height, SilkColor clearColor) =>
        WindowsWglStormContext.Create(width, height, clearColor);
}

[SupportedOSPlatform("windows")]
internal sealed partial class WindowsWglStormContext : IStormGlContext
{
    private const int PfdDrawToWindow = 0x00000004;
    private const int PfdSupportOpenGl = 0x00000020;
    private const int PfdDoubleBuffer = 0x00000001;
    private const byte PfdTypeRgba = 0;
    private const byte PfdMainPlane = 0;
    private const uint CsOwnDc = 0x0020;
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

    private static readonly WndProc WindowProcedure = DefWindowProc;

    private readonly int _width;
    private readonly int _height;
    private readonly nint _window;
    private readonly nint _deviceContext;
    private readonly nint _glContext;
    private readonly GlFunctions _gl;
    private uint _colorTexture;
    private uint _depthRenderbuffer;
    private bool _disposed;

    private WindowsWglStormContext(
        int width,
        int height,
        nint window,
        nint deviceContext,
        nint glContext,
        GlFunctions gl,
        StormOpenGlEvidence openGlEvidence,
        uint framebuffer,
        uint colorTexture,
        uint depthRenderbuffer)
    {
        _width = width;
        _height = height;
        _window = window;
        _deviceContext = deviceContext;
        _glContext = glContext;
        _gl = gl;
        OpenGlEvidence = openGlEvidence;
        Framebuffer = framebuffer;
        _colorTexture = colorTexture;
        _depthRenderbuffer = depthRenderbuffer;
    }

    public uint Framebuffer { get; private set; }

    public StormOpenGlEvidence OpenGlEvidence { get; }

    public static WindowsWglStormContext Create(int width, int height, SilkColor clearColor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        nint module = GetModuleHandle(null);
        string className = $"OpenUsdParityWgl{Environment.ProcessId}";
        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Style = CsOwnDc,
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(WindowProcedure),
            Instance = module,
            ClassName = className
        };
        ushort atom = RegisterClassEx(ref windowClass);
        if (atom == 0)
        {
            int error = Marshal.GetLastPInvokeError();
            if (error != 1410)
            {
                throw new Win32Exception(error, "RegisterClassEx failed for the WGL parity shim.");
            }
        }

        nint window = CreateWindowEx(
            0,
            className,
            "OpenUSD parity WGL",
            0,
            0,
            0,
            1,
            1,
            0,
            0,
            module,
            0);
        if (window == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateWindowEx failed for the WGL parity shim.");
        }

        nint deviceContext = 0;
        nint glContext = 0;
        try
        {
            deviceContext = GetDC(window);
            if (deviceContext == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "GetDC failed for the WGL parity shim.");
            }

            SetPixelFormat(deviceContext);
            glContext = WglCreateContext(deviceContext);
            if (glContext == 0 || !WglMakeCurrent(deviceContext, glContext))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "wglCreateContext or wglMakeCurrent failed.");
            }

            StormOpenGlEvidence openGlEvidence = CaptureOpenGlEvidence();
            GlFunctions gl = GlFunctions.Load();
            uint framebuffer = CreateFramebuffer(gl, width, height, out uint colorTexture, out uint depthRenderbuffer);
            var context = new WindowsWglStormContext(
                width,
                height,
                window,
                deviceContext,
                glContext,
                gl,
                openGlEvidence,
                framebuffer,
                colorTexture,
                depthRenderbuffer);
            context.Clear(clearColor);
            return context;
        }
        catch
        {
            if (glContext != 0)
            {
                _ = WglMakeCurrent(0, 0);
                _ = WglDeleteContext(glContext);
            }
            if (deviceContext != 0)
            {
                _ = ReleaseDC(window, deviceContext);
            }
            _ = DestroyWindow(window);
            throw;
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

        _ = WglMakeCurrent(0, 0);
        _ = WglDeleteContext(_glContext);
        _ = ReleaseDC(_window, _deviceContext);
        _ = DestroyWindow(_window);
    }

    private static void SetPixelFormat(nint deviceContext)
    {
        var descriptor = new PixelFormatDescriptor
        {
            Size = (ushort)Marshal.SizeOf<PixelFormatDescriptor>(),
            Version = 1,
            Flags = PfdDrawToWindow | PfdSupportOpenGl | PfdDoubleBuffer,
            PixelType = PfdTypeRgba,
            ColorBits = 32,
            DepthBits = 24,
            StencilBits = 8,
            LayerType = PfdMainPlane
        };
        int format = ChoosePixelFormat(deviceContext, ref descriptor);
        if (format == 0 || !SetPixelFormat(deviceContext, format, ref descriptor))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetPixelFormat failed for the WGL parity shim.");
        }
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
            throw new InvalidOperationException($"The WGL parity framebuffer is incomplete: 0x{status:X}.");
        }
        return framebuffer;
    }

    private static StormOpenGlEvidence CaptureOpenGlEvidence()
    {
        string? expectedPath = Environment.GetEnvironmentVariable("OPENUSD_MESA_WGL_OPENGL32_PATH");
        ProcessModule? module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().FirstOrDefault(
            candidate => !string.IsNullOrWhiteSpace(expectedPath) &&
                string.Equals(candidate.FileName, expectedPath, StringComparison.OrdinalIgnoreCase)) ??
            Process.GetCurrentProcess().Modules.Cast<ProcessModule>().FirstOrDefault(
                candidate => string.Equals(
                    candidate.ModuleName,
                    "opengl32.dll",
                    StringComparison.OrdinalIgnoreCase));
        string loadedPath = module?.FileName ?? string.Empty;
        string loadedSha256 = File.Exists(loadedPath)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(loadedPath)))
            : string.Empty;
        return new StormOpenGlEvidence(
            loadedPath,
            loadedSha256,
            GetGlString(GlVendor),
            GetGlString(GlRenderer),
            GetGlString(GlVersion),
            FormatHandle(WglGetCurrentDC()),
            FormatHandle(WglGetCurrentContext()));
    }

    private static string GetGlString(uint name)
    {
        nint pointer = GlGetString(name);
        return pointer == 0 ? string.Empty : Marshal.PtrToStringAnsi(pointer) ?? string.Empty;
    }

    private static string FormatHandle(nint handle) =>
        $"0x{unchecked((nuint)handle):X}";

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
            nint address = WglGetProcAddress(name);
            if (address == 0 || address == 1 || address == 2 || address == 3 || address == -1)
            {
                nint module = GetModuleHandle("opengl32.dll");
                address = module == 0 ? 0 : GetProcAddress(module, name);
            }
            if (address == 0)
            {
                throw new InvalidOperationException($"OpenGL function {name} is unavailable in the WGL shim.");
            }
            return Marshal.GetDelegateForFunctionPointer<T>(address);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WndProc(nint window, uint message, nint wParam, nint lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GlGenFramebuffers(int count, ref uint framebuffers);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GlBindFramebuffer(uint target, uint framebuffer);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GlDeleteFramebuffers(int count, ref uint framebuffers);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GlFramebufferTexture2D(
        uint target,
        uint attachment,
        uint textureTarget,
        uint texture,
        int level);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint GlCheckFramebufferStatus(uint target);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GlGenRenderbuffers(int count, ref uint renderbuffers);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GlBindRenderbuffer(uint target, uint renderbuffer);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GlRenderbufferStorage(uint target, uint format, int width, int height);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GlFramebufferRenderbuffer(
        uint target,
        uint attachment,
        uint renderbufferTarget,
        uint renderbuffer);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GlDeleteRenderbuffers(int count, ref uint renderbuffers);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        internal uint Size;
        internal uint Style;
        internal nint WindowProcedure;
        internal int ClassExtra;
        internal int WindowExtra;
        internal nint Instance;
        internal nint Icon;
        internal nint Cursor;
        internal nint Background;
        internal string? MenuName;
        internal string ClassName;
        internal nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PixelFormatDescriptor
    {
        internal ushort Size;
        internal ushort Version;
        internal int Flags;
        internal byte PixelType;
        internal byte ColorBits;
        internal byte RedBits;
        internal byte RedShift;
        internal byte GreenBits;
        internal byte GreenShift;
        internal byte BlueBits;
        internal byte BlueShift;
        internal byte AlphaBits;
        internal byte AlphaShift;
        internal byte AccumBits;
        internal byte AccumRedBits;
        internal byte AccumGreenBits;
        internal byte AccumBlueBits;
        internal byte AccumAlphaBits;
        internal byte DepthBits;
        internal byte StencilBits;
        internal byte AuxBuffers;
        internal byte LayerType;
        internal byte Reserved;
        internal int LayerMask;
        internal int VisibleMask;
        internal int DamageMask;
    }

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true,
        CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowEx(
        int extendedStyle,
        string className,
        string windowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetDC(nint window);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReleaseDC(nint window, nint deviceContext);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial int ChoosePixelFormat(nint deviceContext, ref PixelFormatDescriptor descriptor);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetPixelFormat(
        nint deviceContext,
        int format,
        ref PixelFormatDescriptor descriptor);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string? moduleName);

    [LibraryImport("kernel32.dll", EntryPoint = "GetProcAddress", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetProcAddress(nint module, string procedureName);

    [LibraryImport("opengl32.dll", EntryPoint = "wglCreateContext", SetLastError = true)]
    private static partial nint WglCreateContext(nint deviceContext);

    [LibraryImport("opengl32.dll", EntryPoint = "wglMakeCurrent", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WglMakeCurrent(nint deviceContext, nint glContext);

    [LibraryImport("opengl32.dll", EntryPoint = "wglGetCurrentDC", SetLastError = true)]
    private static partial nint WglGetCurrentDC();

    [LibraryImport("opengl32.dll", EntryPoint = "wglGetCurrentContext", SetLastError = true)]
    private static partial nint WglGetCurrentContext();

    [LibraryImport("opengl32.dll", EntryPoint = "wglDeleteContext", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WglDeleteContext(nint glContext);

    [LibraryImport("opengl32.dll", EntryPoint = "wglGetProcAddress", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint WglGetProcAddress(string procedureName);

    [LibraryImport("opengl32.dll", EntryPoint = "glViewport")]
    private static partial void GlViewport(int x, int y, int width, int height);

    [LibraryImport("opengl32.dll", EntryPoint = "glClearColor")]
    private static partial void GlClearColor(float red, float green, float blue, float alpha);

    [LibraryImport("opengl32.dll", EntryPoint = "glClear")]
    private static partial void GlClear(uint mask);

    [LibraryImport("opengl32.dll", EntryPoint = "glGetString")]
    private static partial nint GlGetString(uint name);

    [LibraryImport("opengl32.dll", EntryPoint = "glFinish")]
    private static partial void GlFinish();

    [LibraryImport("opengl32.dll", EntryPoint = "glReadPixels")]
    private static partial void GlReadPixels(
        int x,
        int y,
        int width,
        int height,
        uint format,
        uint type,
        byte[] pixels);

    [LibraryImport("opengl32.dll", EntryPoint = "glGenTextures")]
    private static partial void GlGenTextures(int count, ref uint textures);

    [LibraryImport("opengl32.dll", EntryPoint = "glBindTexture")]
    private static partial void GlBindTexture(uint target, uint texture);

    [LibraryImport("opengl32.dll", EntryPoint = "glTexParameteri")]
    private static partial void GlTexParameteri(uint target, uint name, int parameter);

    [LibraryImport("opengl32.dll", EntryPoint = "glTexImage2D")]
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

    [LibraryImport("opengl32.dll", EntryPoint = "glDeleteTextures")]
    private static partial void GlDeleteTextures(int count, ref uint textures);
}
