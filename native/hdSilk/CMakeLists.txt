set(OPENUSD_HDSILK_SOURCES
    ../include/openusd_render_camera.h
    ../include/openusd_mdl.h
    include/openusd_hdsilk.h
    ../private/openusd_render_camera_internal.h
    src/api.cpp
    src/basisCurves.h
    src/basisCurves.cpp
    src/curveWidths.h
    src/curveWidths.cpp
    src/implicitSurfaceSceneIndexPlugin.h
    src/implicitSurfaceSceneIndexPlugin.cpp
    src/instanceLinking.h
    src/instanceLinking.cpp
    src/instancer.h
    src/instancer.cpp
    src/light.h
    src/light.cpp
    src/lightLinkingSceneIndexPlugin.h
    src/lightLinkingSceneIndexPlugin.cpp
    src/material.h
    src/material.cpp
    src/materialXBridge.h
    src/materialXBridge.cpp
    src/mdlAdapter.h
    src/mdlAdapter.cpp
    src/mdlMaterialSceneIndexPlugin.h
    src/mdlMaterialSceneIndexPlugin.cpp
    src/mesh.h
    src/mesh.cpp
    src/meshVertexLayout.h
    src/meshPreparation.h
    src/pageWriter.h
    src/points.h
    src/points.cpp
    src/renderDelegate.h
    src/renderDelegate.cpp
    src/rendererPlugin.h
    src/rendererPlugin.cpp
    src/renderPass.h
    src/renderPass.cpp
    src/sceneState.h
    src/sceneState.cpp
    src/subdivision.h
    src/subdivision.cpp)

find_package(MaterialX CONFIG REQUIRED)

# OpenSubdiv ships inside the pinned OpenUSD install prefix but exports no CMake
# package, and pxrTargets does not re-export it: OpenUSD links osdCPU privately
# into its monolithic library, so its symbols never reach a consumer. hdSilk
# evaluates subdivision surfaces itself and therefore resolves the same pinned
# static library directly from that prefix. Its headers are already reachable
# through the pxr include directory.
find_library(OPENUSD_OPENSUBDIV_CPU_LIBRARY
    NAMES osdCPU
    HINTS "${pxr_DIR}/lib" "${pxr_DIR}/lib64"
    NO_DEFAULT_PATH)
if(NOT OPENUSD_OPENSUBDIV_CPU_LIBRARY)
    message(
        FATAL_ERROR
        "hdSilk requires the OpenSubdiv osdCPU library from the pinned OpenUSD "
        "install prefix (${pxr_DIR}).")
endif()

option(OPENUSD_WITH_MATERIALX_SPIRV
    "Compile portable MaterialX SPIR-V payloads without requiring a GPU runtime"
    ${OPENUSD_WITH_VULKAN})
set(OPENUSD_MATERIALX_SHADERC_ROOT "" CACHE PATH
    "Private locked shaderc compiler prefix for builds without Vulkan")
# The existing Vulkan names describe the generated shader dialect, not a GPU
# runtime dependency. Native probes use the same real compiler capability.
set(OPENUSD_HDSILK_MATERIALX_VULKAN_LIBS)
if(OPENUSD_WITH_MATERIALX_SPIRV)
    if(NOT TARGET MaterialXGenGlsl)
        message(FATAL_ERROR "MaterialX SPIR-V generation requires MaterialXGenGlsl.")
    endif()
    if(OPENUSD_WITH_VULKAN)
        set(OPENUSD_HDSILK_SHADERC_TARGET Vulkan::shaderc_combined)
    else()
        if(NOT OPENUSD_MATERIALX_SHADERC_ROOT)
            message(FATAL_ERROR
                "MaterialX SPIR-V generation requires the private locked shaderc prefix.")
        endif()
        find_path(OPENUSD_MATERIALX_SHADERC_INCLUDE_DIR
            NAMES shaderc/shaderc.hpp
            PATHS "${OPENUSD_MATERIALX_SHADERC_ROOT}/include"
            NO_DEFAULT_PATH NO_CACHE REQUIRED)
        find_library(OPENUSD_MATERIALX_SHADERC_LIBRARY
            NAMES shaderc_combined
            PATHS "${OPENUSD_MATERIALX_SHADERC_ROOT}/lib"
            NO_DEFAULT_PATH NO_CACHE REQUIRED)
        if(NOT OPENUSD_MATERIALX_SHADERC_LIBRARY MATCHES "\\.(a|lib)$")
            message(FATAL_ERROR "The private MaterialX shader compiler must be static.")
        endif()
        find_package(Threads REQUIRED)
        add_library(openusd_materialx_shaderc STATIC IMPORTED)
        set_target_properties(openusd_materialx_shaderc PROPERTIES
            IMPORTED_LOCATION "${OPENUSD_MATERIALX_SHADERC_LIBRARY}"
            INTERFACE_INCLUDE_DIRECTORIES "${OPENUSD_MATERIALX_SHADERC_INCLUDE_DIR}"
            INTERFACE_LINK_LIBRARIES Threads::Threads)
        set(OPENUSD_HDSILK_SHADERC_TARGET openusd_materialx_shaderc)
    endif()
    list(APPEND OPENUSD_HDSILK_MATERIALX_VULKAN_LIBS
        MaterialXGenGlsl ${OPENUSD_HDSILK_SHADERC_TARGET})
endif()

set(OPENUSD_HDSILK_MATERIALX_MSL_LIBS)
if(TARGET MaterialXGenMsl)
    list(APPEND OPENUSD_HDSILK_MATERIALX_MSL_LIBS MaterialXGenMsl)
endif()

function(configure_openusd_hdsilk_target target_name core_target)
    target_compile_features(${target_name} PRIVATE cxx_std_17)
    target_compile_definitions(${target_name} PRIVATE OPENUSD_HDSILK_BUILD)
    if(OPENUSD_HDSILK_MATERIALX_VULKAN_LIBS)
        target_compile_definitions(
            ${target_name}
            PRIVATE OPENUSD_HDSILK_WITH_MATERIALX_VULKAN)
    endif()
    if(OPENUSD_HDSILK_MATERIALX_MSL_LIBS)
        target_compile_definitions(
            ${target_name}
            PRIVATE OPENUSD_HDSILK_WITH_MATERIALX_MSL)
    endif()
    target_include_directories(${target_name}
        PUBLIC
            $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/../include>
            $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
            $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/../openusd_dotnet/include>
            $<INSTALL_INTERFACE:include>
        PRIVATE
            ${CMAKE_CURRENT_SOURCE_DIR}/src
            ${CMAKE_CURRENT_SOURCE_DIR}/../private
            ${CMAKE_CURRENT_SOURCE_DIR}/../openusd_dotnet/private)
    target_link_libraries(
        ${target_name}
        PRIVATE
            ${core_target}
            usd_m
            hio
            ${OPENUSD_OPENSUBDIV_CPU_LIBRARY}
            MaterialXFormat
            ${OPENUSD_HDSILK_MATERIALX_VULKAN_LIBS}
            ${OPENUSD_HDSILK_MATERIALX_MSL_LIBS})
    if(MSVC)
        target_compile_options(${target_name} PRIVATE /W4 /WX /permissive-)
    else()
        target_compile_options(${target_name} PRIVATE -Wall -Wextra -Wpedantic -Werror)
    endif()
endfunction()

add_library(openusd_hdsilk SHARED ${OPENUSD_HDSILK_SOURCES})
configure_openusd_hdsilk_target(openusd_hdsilk openusd_dotnet)

# The plugInfo.json LibraryPath is relative to the plugin root
# (<prefix>/plugin/usd/hdSilk, per "Root": ".."), so it must walk back up to
# the shim install prefix before descending into the directory that holds
# the built shared library.
set(HDSILK_LIBRARY_FILE_NAME "${CMAKE_SHARED_LIBRARY_PREFIX}openusd_hdsilk${CMAKE_SHARED_LIBRARY_SUFFIX}")
if(WIN32)
    set(HDSILK_LIBRARY_RELATIVE_DIR "bin")
else()
    set(HDSILK_LIBRARY_RELATIVE_DIR "lib")
endif()
set(HDSILK_PLUGIN_LIBRARY_PATH "../../../${HDSILK_LIBRARY_RELATIVE_DIR}/${HDSILK_LIBRARY_FILE_NAME}")

set(HDSILK_CONFIGURED_PLUGINFO ${CMAKE_CURRENT_BINARY_DIR}/resources/plugInfo.json)
configure_file(
    resources/plugInfo.json.in
    ${HDSILK_CONFIGURED_PLUGINFO}
    @ONLY)

include(GNUInstallDirs)
install(TARGETS openusd_hdsilk
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
    LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}
    ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR})
install(FILES include/openusd_hdsilk.h DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
install(FILES ${HDSILK_CONFIGURED_PLUGINFO} DESTINATION plugin/usd/hdSilk/resources)

# The hdSilk probe links openusd_dotnet_renderer_test, which only exists when the
# native test targets are built, so this must follow OPENUSD_BUILD_NATIVE_TESTS.
# The fuzzer configure turns native tests off and would otherwise fail generation.
option(OPENUSD_HDSILK_BUILD_TESTS
    "Build the hdSilk native probe"
    ${OPENUSD_BUILD_NATIVE_TESTS})
if(OPENUSD_HDSILK_BUILD_TESTS)
    add_library(openusd_hdsilk_test SHARED ${OPENUSD_HDSILK_SOURCES})
    configure_openusd_hdsilk_target(
        openusd_hdsilk_test
        openusd_dotnet_renderer_test)
    target_compile_definitions(
        openusd_hdsilk_test
        PRIVATE
            OPENUSD_HDSILK_ENABLE_TEST_HOOKS
            OPENUSD_RENDERER_ENABLE_TEST_HOOKS)

    set(HDSILK_TEST_LIBRARY_FILE_NAME
        "${CMAKE_SHARED_LIBRARY_PREFIX}openusd_hdsilk_test${CMAKE_SHARED_LIBRARY_SUFFIX}")
    set(HDSILK_PLUGIN_LIBRARY_PATH
        "../../../${HDSILK_LIBRARY_RELATIVE_DIR}/${HDSILK_TEST_LIBRARY_FILE_NAME}")
    set(HDSILK_TEST_CONFIGURED_PLUGINFO
        ${CMAKE_CURRENT_BINARY_DIR}/test-resources/plugInfo.json)
    configure_file(
        resources/plugInfo.json.in
        ${HDSILK_TEST_CONFIGURED_PLUGINFO}
        @ONLY)

    enable_testing()
    add_subdirectory(tests)
endif()
