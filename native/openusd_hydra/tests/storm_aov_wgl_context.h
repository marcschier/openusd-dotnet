// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_STORM_AOV_WGL_CONTEXT_H
#define OPENUSD_STORM_AOV_WGL_CONTEXT_H

#include <Windows.h>
#include <gl/GL.h>

#include <cstring>
#include <iostream>

class StormAovWglContext final
{
public:
    StormAovWglContext() = default;
    StormAovWglContext(const StormAovWglContext&) = delete;
    StormAovWglContext& operator=(const StormAovWglContext&) = delete;

    bool Create()
    {
        WNDCLASSA window_class{};
        window_class.style = CS_OWNDC;
        window_class.lpfnWndProc = DefWindowProcA;
        window_class.hInstance = GetModuleHandleA(nullptr);
        window_class.lpszClassName = ClassName;
        _class_atom = RegisterClassA(&window_class);
        if (_class_atom == 0 && GetLastError() != ERROR_CLASS_ALREADY_EXISTS)
        {
            return false;
        }
        _window = CreateWindowExA(
            WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW,
            ClassName, ClassName, WS_POPUP,
            0, 0, 64, 64, nullptr, nullptr, window_class.hInstance, nullptr);
        if (_window == nullptr)
        {
            return false;
        }
        _device = GetDC(_window);
        if (_device == nullptr)
        {
            return false;
        }
        PIXELFORMATDESCRIPTOR descriptor{};
        descriptor.nSize = sizeof(descriptor);
        descriptor.nVersion = 1;
        descriptor.dwFlags =
            PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER;
        descriptor.iPixelType = PFD_TYPE_RGBA;
        descriptor.cColorBits = 32;
        descriptor.cDepthBits = 24;
        descriptor.iLayerType = PFD_MAIN_PLANE;
        const int format = ChoosePixelFormat(_device, &descriptor);
        if (format == 0 || !SetPixelFormat(_device, format, &descriptor))
        {
            return false;
        }
        _context = wglCreateContext(_device);
        return _context != nullptr && MakeCurrent();
    }

    bool MakeCurrent() const
    {
        return wglMakeCurrent(_device, _context) != FALSE;
    }

    static void ClearCurrent()
    {
        wglMakeCurrent(nullptr, nullptr);
    }

    static void PrintDriver()
    {
        const char* renderer =
            reinterpret_cast<const char*>(glGetString(GL_RENDERER));
        const char* vendor =
            reinterpret_cast<const char*>(glGetString(GL_VENDOR));
        const char* version =
            reinterpret_cast<const char*>(glGetString(GL_VERSION));
        char module[MAX_PATH]{};
        GetModuleFileNameA(
            GetModuleHandleA("opengl32.dll"), module, MAX_PATH);
        const bool software = renderer != nullptr &&
            (std::strstr(renderer, "llvmpipe") != nullptr ||
             std::strstr(renderer, "softpipe") != nullptr ||
             std::strstr(renderer, "GDI Generic") != nullptr ||
             std::strstr(renderer, "Software") != nullptr);
        const bool hardware = !software && vendor != nullptr &&
            (std::strstr(vendor, "NVIDIA") != nullptr ||
             std::strstr(vendor, "Intel") != nullptr ||
             std::strstr(vendor, "ATI") != nullptr ||
             std::strstr(vendor, "AMD") != nullptr);
        std::cout << "GL_VENDOR=" << (vendor == nullptr ? "absent" : vendor)
                  << "\nGL_RENDERER="
                  << (renderer == nullptr ? "absent" : renderer)
                  << "\nGL_VERSION=" << (version == nullptr ? "absent" : version)
                  << "\nGL_MODULE=" << module
                  << "\nGL_DRIVER_CLASS="
                  << (software ? "software" : (hardware ? "hardware" : "unclassified"))
                  << "\nWINDOWS_HIDDEN_NO_ACTIVATION=true\n";
        for (const char* name : {"usd_ms.dll", "openusd_dotnet.dll", "openusd_hydra.dll"})
        {
            const HMODULE handle = GetModuleHandleA(name);
            if (handle != nullptr)
            {
                GetModuleFileNameA(handle, module, MAX_PATH);
                std::cout << "LOADED_MODULE=" << name << "," << module << "\n";
            }
        }
    }

    ~StormAovWglContext()
    {
        if (_context != nullptr)
        {
            if (wglGetCurrentContext() == _context)
            {
                ClearCurrent();
            }
            wglDeleteContext(_context);
        }
        if (_device != nullptr && _window != nullptr)
        {
            ReleaseDC(_window, _device);
        }
        if (_window != nullptr)
        {
            DestroyWindow(_window);
        }
        if (_class_atom != 0)
        {
            UnregisterClassA(ClassName, GetModuleHandleA(nullptr));
        }
    }

private:
    static constexpr char ClassName[] = "OpenUsdPrivateStormAovProbe";
    ATOM _class_atom = 0;
    HWND _window = nullptr;
    HDC _device = nullptr;
    HGLRC _context = nullptr;
};

#endif
