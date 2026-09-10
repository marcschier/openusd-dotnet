// Copyright (c) marcschier. Licensed under the MIT License.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
internal sealed unsafe partial class WindowsContext : IDisposable
{
    private const string ClassName = "OpenUsdPublicAovPackageProbe";
    private nint _window;
    private nint _device;
    private nint _context;
    private ushort _atom;

    internal WindowsContext()
    {
        fixed (char* className = ClassName)
        {
            WindowClass windowClass = new()
            {
                Size = (uint)sizeof(WindowClass),
                Style = 0x20,
                WindowProcedure = &WindowProcedure,
                Instance = GetModuleHandle(null),
                ClassName = className,
            };
            _atom = RegisterClassEx(in windowClass);
            if (_atom == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "RegisterClassExW failed.");
            }
            try
            {
                _window = CreateWindowEx(
                    0x08000080, className, className, 0x80000000,
                    0, 0, 64, 64, 0, 0, windowClass.Instance, 0);
                Require(_window != 0, "CreateWindowExW");
                _device = GetDC(_window);
                Require(_device != 0, "GetDC");
                PixelFormat descriptor = new()
                {
                    Size = (ushort)sizeof(PixelFormat),
                    Version = 1,
                    Flags = 0x25,
                    ColorBits = 32,
                    DepthBits = 24,
                };
                int format = ChoosePixelFormat(_device, in descriptor);
                Require(format != 0 && SetPixelFormat(_device, format, in descriptor) != 0, "SetPixelFormat");
                _context = WglCreateContext(_device);
                Require(_context != 0 && WglMakeCurrent(_device, _context) != 0, "wglMakeCurrent");
            }
            catch
            {
                Dispose();
                throw;
            }
        }
    }

    internal nint Window => _window;

    internal static string Driver =>
        Marshal.PtrToStringAnsi(GlGetString(0x1F01)) ??
        throw new InvalidOperationException("No OpenGL renderer string.");

    public void Dispose()
    {
        if (_context != 0)
        {
            Require(WglMakeCurrent(0, 0) != 0, "wglMakeCurrent(clear)");
            Require(WglDeleteContext(_context) != 0, "wglDeleteContext");
            _context = 0;
        }
        if (_device != 0)
        {
            Require(ReleaseDC(_window, _device) != 0, "ReleaseDC");
            _device = 0;
        }
        if (_window != 0)
        {
            Require(DestroyWindow(_window) != 0, "DestroyWindow");
            _window = 0;
        }
        if (_atom != 0)
        {
            fixed (char* className = ClassName)
            {
                Require(UnregisterClass(className, GetModuleHandle(null)) != 0, "UnregisterClassW");
            }
            _atom = 0;
        }
    }

    private static void Require(bool success, string operation)
    {
        if (!success)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), operation);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam) =>
        DefWindowProc(window, message, wParam, lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
    {
        internal uint Size;
        internal uint Style;
        internal delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint> WindowProcedure;
        internal int ClassExtra;
        internal int WindowExtra;
        internal nint Instance;
        internal nint Icon;
        internal nint Cursor;
        internal nint Background;
        internal char* MenuName;
        internal char* ClassName;
        internal nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PixelFormat
    {
        internal ushort Size;
        internal ushort Version;
        internal uint Flags;
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
        internal uint LayerMask;
        internal uint VisibleMask;
        internal uint DamageMask;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    private static partial nint GetModuleHandle(char* name);
    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    private static partial ushort RegisterClassEx(in WindowClass windowClass);
    [LibraryImport("user32.dll", EntryPoint = "UnregisterClassW", SetLastError = true)]
    private static partial int UnregisterClass(char* className, nint instance);
    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true)]
    private static partial nint CreateWindowEx(
        uint extendedStyle, char* className, char* title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetDC(nint window);
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int ReleaseDC(nint window, nint device);
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int DestroyWindow(nint window);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial int ChoosePixelFormat(nint device, in PixelFormat descriptor);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial int SetPixelFormat(nint device, int format, in PixelFormat descriptor);
    [LibraryImport("opengl32.dll", EntryPoint = "wglCreateContext", SetLastError = true)]
    private static partial nint WglCreateContext(nint device);
    [LibraryImport("opengl32.dll", EntryPoint = "wglMakeCurrent", SetLastError = true)]
    private static partial int WglMakeCurrent(nint device, nint context);
    [LibraryImport("opengl32.dll", EntryPoint = "wglDeleteContext", SetLastError = true)]
    private static partial int WglDeleteContext(nint context);
    [LibraryImport("opengl32.dll", EntryPoint = "glGetString")]
    private static partial nint GlGetString(uint name);
}
