/* Copyright (c) marcschier. Licensed under the MIT License. */

#include "openusd_storm_aov.h"

#include <stddef.h>
#include <stdio.h>
#include <math.h>
#include <string.h>
#include <Windows.h>
#include <gl/GL.h>

_Static_assert(OPENUSD_STORM_ABI_VERSION == 9, "Public AOVs require Storm ABI 9");
_Static_assert(sizeof(openusd_storm_aov_request) == 640, "request ABI");
_Static_assert(sizeof(openusd_storm_aov_output) == 64, "output ABI");
_Static_assert(sizeof(openusd_storm_aov_identity) == 56, "identity ABI");
_Static_assert(sizeof(openusd_storm_aov_instance_context) == 24, "context ABI");
_Static_assert(sizeof(openusd_storm_aov_view) == 688, "view ABI");
_Static_assert(offsetof(openusd_storm_aov_request, camera) == 112, "request camera ABI");
_Static_assert(offsetof(openusd_storm_aov_view, applied_camera) == 160, "applied camera ABI");

static int CheckRender(const char* plugins, const char* stage)
{
    typedef void (APIENTRY* BindFramebuffer)(GLenum, GLuint);
    static const char class_name[] = "OpenUsdPublicAovC11";
    WNDCLASSA window_class = {0};
    HWND window = NULL;
    HDC device = NULL;
    HGLRC context = NULL;
    PIXELFORMATDESCRIPTOR pixel_format = {0};
    openusd_storm_renderer* renderer = NULL;
    openusd_storm_aov_owner* owner = NULL;
    openusd_storm_aov_request request = {0};
    openusd_storm_aov_view view = {0};
    char text[2048] = {0};
    openusd_error_buffer error = {text, sizeof(text), 0};
    unsigned char before[64 * 64 * 4] = {0};
    unsigned char after[64 * 64 * 4] = {0};
    int32_t converged = 0;
    int passed = 0;
    int iteration;
    int index;
    float depth = 0;
    const char* phase = "window";
    BindFramebuffer bind_framebuffer = NULL;
    window_class.style = CS_OWNDC;
    window_class.lpfnWndProc = DefWindowProcA;
    window_class.hInstance = GetModuleHandleA(NULL);
    window_class.lpszClassName = class_name;
    if (!RegisterClassA(&window_class))
    {
        return 1;
    }
    window = CreateWindowExA(
        WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW, class_name, class_name,
        WS_POPUP, 0, 0, 64, 64, NULL, NULL, window_class.hInstance, NULL);
    if (window == NULL || (device = GetDC(window)) == NULL)
    {
        goto cleanup;
    }
    pixel_format.nSize = sizeof(pixel_format);
    pixel_format.nVersion = 1;
    pixel_format.dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER;
    pixel_format.iPixelType = PFD_TYPE_RGBA;
    pixel_format.cColorBits = 32;
    pixel_format.cDepthBits = 24;
    index = ChoosePixelFormat(device, &pixel_format);
    if (index == 0 || !SetPixelFormat(device, index, &pixel_format))
    {
        goto cleanup;
    }
    context = wglCreateContext(device);
    phase = "context-and-renderer";
    if (context == NULL || !wglMakeCurrent(device, context) ||
        openusd_storm_create(plugins, stage, &renderer, &error) != OPENUSD_STATUS_OK)
    {
        goto cleanup;
    }
    {
        PROC address = wglGetProcAddress("glBindFramebuffer");
        if (address == NULL)
        {
            goto cleanup;
        }
        memcpy(&bind_framebuffer, &address, sizeof(bind_framebuffer));
    }
    request.struct_size = sizeof(request);
    request.version = OPENUSD_STORM_AOV_VERSION;
    request.width = 64;
    request.height = 64;
    request.output_count = 2;
    request.output_kinds[0] = OPENUSD_STORM_AOV_COLOR;
    request.output_kinds[1] = OPENUSD_STORM_AOV_DEPTH;
    request.max_pixels = 4096;
    request.max_working_bytes = 1048576;
    request.camera.struct_size = sizeof(request.camera);
    request.camera.mode = OPENUSD_RENDER_CAMERA_MODE_MATRICES;
    for (index = 0; index < 16; ++index)
    {
        request.camera.view[index] = index % 5 == 0 ? 1.0 : 0.0;
    }
    request.camera.projection[0] = 0.25;
    request.camera.projection[5] = 0.25;
    request.camera.projection[10] = -0.2;
    request.camera.projection[14] = -1.2;
    request.camera.projection[15] = 1;
    phase = "invalid-requests";
    for (index = 0; index < 8; ++index)
    {
        openusd_storm_aov_request invalid = request;
        openusd_status expected = OPENUSD_STATUS_INVALID_ARGUMENT;
        switch (index)
        {
        case 0: invalid.struct_size--; break;
        case 1: invalid.version++; break;
        case 2: invalid.output_count = 9; break;
        case 3: invalid.output_kinds[0] = UINT32_MAX; break;
        case 4: invalid.output_kinds[1] = invalid.output_kinds[0]; break;
        case 5: invalid.width = 0; break;
        case 6:
            invalid.width = 65;
            expected = OPENUSD_STATUS_BUFFER_TOO_SMALL;
            break;
        case 7:
            invalid.max_working_bytes = 1;
            expected = OPENUSD_STATUS_BUFFER_TOO_SMALL;
            break;
        }
        owner = (openusd_storm_aov_owner*)(uintptr_t)1;
        if (openusd_storm_aov_capture(renderer, &invalid, &owner, &error) != expected ||
            owner != NULL || error.required == 0)
        {
            owner = NULL;
            goto cleanup;
        }
    }
    phase = "ordinary-render";
    for (iteration = 0; iteration < 32 && !converged; ++iteration)
    {
        if (openusd_storm_render(renderer, 64, 64, 0, 0,
                &request.camera, &converged, &error) != OPENUSD_STATUS_OK)
        {
            goto cleanup;
        }
    }
    if (!converged)
    {
        goto cleanup;
    }
    glFinish();
    bind_framebuffer(0x8D40, 0);
    glReadBuffer(GL_BACK);
    glReadPixels(0, 0, 64, 64, GL_RGBA, GL_UNSIGNED_BYTE, before);
    if (openusd_storm_render(renderer, 64, 64, 0, 0,
            &request.camera, &converged, &error) != OPENUSD_STATUS_OK)
    {
        goto cleanup;
    }
    glFinish();
    bind_framebuffer(0x8D40, 0);
    glReadBuffer(GL_BACK);
    glReadPixels(0, 0, 64, 64, GL_RGBA, GL_UNSIGNED_BYTE, after);
    if (memcmp(before, after, sizeof(before)) != 0)
    {
        goto cleanup;
    }
    memcpy(before, after, sizeof(before));
    phase = "capture";
    if (openusd_storm_aov_capture(renderer, &request, &owner, &error) != OPENUSD_STATUS_OK)
    {
        goto cleanup;
    }
    view.struct_size = sizeof(view);
    view.version = OPENUSD_STORM_AOV_VERSION;
    phase = "get-view";
    if (openusd_storm_aov_get_view(owner, &view, &error) != OPENUSD_STATUS_OK ||
        view.output_count != 2 || view.outputs[1].kind != OPENUSD_STORM_AOV_DEPTH ||
        view.outputs[1].format != OPENUSD_STORM_AOV_FORMAT_FLOAT32)
    {
        goto cleanup;
    }
    memcpy(&depth, (const unsigned char*)view.pixel_data +
        view.outputs[1].data_offset + (31 * 64 + 16) * sizeof(float), sizeof(depth));
    glFinish();
    bind_framebuffer(0x8D40, 0);
    glReadBuffer(GL_BACK);
    glReadPixels(0, 0, 64, 64, GL_RGBA, GL_UNSIGNED_BYTE, after);
    phase = "depth-and-rgba";
    if (fabsf(depth - 0.2f) > 0.00001f || memcmp(before, after, sizeof(before)) != 0)
    {
        fprintf(stderr, "C11 depth=%f rgbaDifference=%d glError=%u\n",
            (double)depth, memcmp(before, after, sizeof(before)), glGetError());
        for (index = 0; index < (int)sizeof(before); ++index)
        {
            if (before[index] != after[index])
            {
                fprintf(stderr, "C11 first difference byte=%d before=%u after=%u\n",
                    index, (unsigned int)before[index], (unsigned int)after[index]);
                break;
            }
        }
        goto cleanup;
    }
    phase = "restored-presentation";
    if (openusd_storm_render(renderer, 64, 64, 0, 0,
            &request.camera, &converged, &error) != OPENUSD_STATUS_OK)
    {
        goto cleanup;
    }
    glFinish();
    bind_framebuffer(0x8D40, 0);
    glReadBuffer(GL_BACK);
    glReadPixels(0, 0, 64, 64, GL_RGBA, GL_UNSIGNED_BYTE, after);
    if (memcmp(before, after, sizeof(before)) != 0)
    {
        goto cleanup;
    }
    phase = "destroy";
    if (openusd_storm_destroy(renderer, &error) != OPENUSD_STATUS_OK)
    {
        goto cleanup;
    }
    renderer = NULL;
    wglMakeCurrent(NULL, NULL);
    view.struct_size = sizeof(view);
    view.version = OPENUSD_STORM_AOV_VERSION;
    phase = "detached-view";
    if (openusd_storm_aov_get_view(owner, &view, &error) != OPENUSD_STATUS_OK)
    {
        goto cleanup;
    }
    passed = 1;
    puts("PUBLIC_C11_RENDER=passed; invalid-inputs=8; depth=.2; rgba=exact; owner=detached");
cleanup:
    openusd_storm_aov_release(owner);
    if (renderer != NULL)
    {
        wglMakeCurrent(device, context);
        openusd_storm_release(renderer);
    }
    wglMakeCurrent(NULL, NULL);
    if (context != NULL) { wglDeleteContext(context); }
    if (device != NULL) { ReleaseDC(window, device); }
    if (window != NULL) { DestroyWindow(window); }
    UnregisterClassA(class_name, window_class.hInstance);
    if (!passed) { fprintf(stderr, "Public C11 render failed (%s): %s\n", phase, text); }
    return passed ? 0 : 1;
}

int main(int argc, char** argv)
{
    openusd_storm_aov_owner* owner = (openusd_storm_aov_owner*)(uintptr_t)1;
    openusd_storm_aov_view view = {0};
    char text[256] = {0};
    openusd_error_buffer error = {text, sizeof(text), 0};
    view.struct_size = sizeof(view);
    view.version = OPENUSD_STORM_AOV_VERSION;
    if (openusd_storm_get_abi_version() != 9 ||
        openusd_storm_aov_capture(NULL, NULL, &owner, &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        owner != NULL ||
        openusd_storm_aov_get_view(NULL, &view, &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        view.struct_size != 0 || view.pixel_data != NULL || view.outputs != NULL ||
        view.pixel_bytes != 0 || view.identity_count != 0)
    {
        fprintf(stderr, "Public Storm ABI9 boundary failed: %s\n", text);
        return 1;
    }
    openusd_storm_aov_release(NULL);
    puts("PUBLIC_STORM_ABI9_C11=passed; layout=640/64/56/24/688; outputs=zero-on-error");
    if (argc != 3)
    {
        fputs("Usage: probe <plugin-path> <planar-stage>\n", stderr);
        return 2;
    }
    return CheckRender(argv[1], argv[2]);
}
