add_executable(
    hdsilk_probe
    hdsilk_probe.cpp
    ../src/sceneState.cpp)
target_compile_features(hdsilk_probe PRIVATE cxx_std_17)
target_link_libraries(
    hdsilk_probe
    PRIVATE openusd_hdsilk_test openusd_dotnet_renderer_test usd_m)
target_include_directories(
    hdsilk_probe
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}
        ${CMAKE_CURRENT_SOURCE_DIR}/../src
        ${CMAKE_CURRENT_SOURCE_DIR}/../../openusd_dotnet/tests)

if(MSVC)
    target_compile_options(hdsilk_probe PRIVATE /W4 /WX /permissive-)
else()
    target_compile_options(hdsilk_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

# Assemble a self-contained runtime plugin directory for the probe: merge
# the installed OpenUSD plugin registry (lib/usd, which owns the top-level
# plugInfo.json with "Includes": ["*/resources/"], plus plugin/usd) with our
# own hdSilk plugin metadata and shared library, mirroring the relative
# layout used by the shim install prefix
# (plugin/usd/hdSilk/resources/plugInfo.json next to the platform library
# directory).
# The extra "plugin" level below is required, not cosmetic: plugInfo.json's
# LibraryPath is relative to the resolved plugin root
# (Root: ".." from hdSilk/resources -> hdSilk/), which must sit exactly
# 3 directories below the runtime root -- matching plugin/usd/hdSilk under
# the real shim install prefix -- for the relative path to reach bin/ or lib/.
set(HDSILK_PROBE_RUNTIME_DIR ${CMAKE_CURRENT_BINARY_DIR}/runtime)
set(HDSILK_PROBE_PLUGIN_DIR ${HDSILK_PROBE_RUNTIME_DIR}/plugin/usd)
set(HDSILK_PROBE_NATIVE_DIR
    ${HDSILK_PROBE_RUNTIME_DIR}/${HDSILK_LIBRARY_RELATIVE_DIR})
set(HDSILK_PROBE_STAMP ${HDSILK_PROBE_RUNTIME_DIR}/runtime.stamp)

add_custom_command(
    OUTPUT ${HDSILK_PROBE_STAMP}
    COMMAND ${CMAKE_COMMAND} -E make_directory ${HDSILK_PROBE_PLUGIN_DIR}
    COMMAND ${CMAKE_COMMAND} -E copy_directory ${pxr_DIR}/lib/usd ${HDSILK_PROBE_PLUGIN_DIR}
    COMMAND ${CMAKE_COMMAND} -E copy_directory ${pxr_DIR}/plugin/usd ${HDSILK_PROBE_PLUGIN_DIR}
    COMMAND ${CMAKE_COMMAND} -E make_directory ${HDSILK_PROBE_PLUGIN_DIR}/hdSilk/resources
    COMMAND ${CMAKE_COMMAND} -E copy
        ${HDSILK_TEST_CONFIGURED_PLUGINFO}
        ${HDSILK_PROBE_PLUGIN_DIR}/hdSilk/resources/plugInfo.json
    COMMAND ${CMAKE_COMMAND} -E make_directory ${HDSILK_PROBE_NATIVE_DIR}
    COMMAND ${CMAKE_COMMAND} -E copy
        $<TARGET_FILE:openusd_hdsilk_test>
        ${HDSILK_PROBE_NATIVE_DIR}/$<TARGET_FILE_NAME:openusd_hdsilk_test>
    COMMAND ${CMAKE_COMMAND} -E copy
        $<TARGET_FILE:openusd_dotnet_renderer_test>
        ${HDSILK_PROBE_NATIVE_DIR}/$<TARGET_FILE_NAME:openusd_dotnet_renderer_test>
    COMMAND ${CMAKE_COMMAND} -E touch ${HDSILK_PROBE_STAMP}
    DEPENDS
        openusd_hdsilk_test
        openusd_dotnet_renderer_test
        ${HDSILK_TEST_CONFIGURED_PLUGINFO}
    COMMENT "Assembling hdSilk probe runtime plugin directory")

add_custom_target(hdsilk_probe_runtime DEPENDS ${HDSILK_PROBE_STAMP})
add_dependencies(hdsilk_probe hdsilk_probe_runtime)

get_filename_component(HDSILK_PROBE_STAGE "${CMAKE_CURRENT_SOURCE_DIR}/../../../test-assets/hdsilk-probe-stage.usda" ABSOLUTE)

if(WIN32)
    # Note: this PATH deliberately does NOT include the original build
    # output directory of openusd_hdsilk (i.e. $<TARGET_FILE_DIR:...>).
    # hdsilk_probe.exe implicitly imports openusd_hdsilk.dll at process
    # startup, and the Plug system separately dlopen()s the copy at
    # <runtime>/bin via plugInfo.json's LibraryPath. If both directories
    # were on PATH, Windows would load two distinct on-disk copies of the
    # same DLL as separate modules, running static initializers (and this
    # plugin's TF_REGISTRY_FUNCTION TfType::Define call) twice. Keeping only
    # the <runtime>/bin copy on PATH ensures both loads resolve to the same
    # file/module.
    #
    # Use ENVIRONMENT_MODIFICATION (not ENVIRONMENT) so each directory is a
    # separate path_list_prepend operation: the ENVIRONMENT property treats
    # its value as a plain CMake list split on ';', which corrupts a
    # semicolon-delimited Windows PATH string built via string
    # concatenation.
    set(HDSILK_PROBE_ENV_MODS
        "PATH=path_list_prepend:${HDSILK_PROBE_RUNTIME_DIR}/bin"
        "PATH=path_list_prepend:${pxr_DIR}/lib"
        "PATH=path_list_prepend:${pxr_DIR}/bin")
elseif(APPLE)
    # The same double-load hazard as the Windows branch above. The probe links
    # openusd_hdsilk_test through @rpath while the Plug system separately
    # dlopen()s the copy under <runtime>. Pointing DYLD_LIBRARY_PATH at the
    # runtime copy makes both resolve to one image. dyld ignores
    # LD_LIBRARY_PATH, which is why the probe previously loaded the library
    # twice and failed with "TfType ... already has a defined C++ type".
    set(HDSILK_PROBE_ENV_MODS
        "DYLD_LIBRARY_PATH=path_list_prepend:${HDSILK_PROBE_NATIVE_DIR}"
        "DYLD_LIBRARY_PATH=path_list_prepend:${pxr_DIR}/lib")
else()
    set(HDSILK_PROBE_ENV_MODS
        "LD_LIBRARY_PATH=path_list_prepend:${HDSILK_PROBE_NATIVE_DIR}"
        "LD_LIBRARY_PATH=path_list_prepend:${pxr_DIR}/lib")
endif()

add_test(NAME hdsilk_probe
    COMMAND hdsilk_probe ${HDSILK_PROBE_PLUGIN_DIR} ${HDSILK_PROBE_STAGE})
set_tests_properties(hdsilk_probe PROPERTIES ENVIRONMENT_MODIFICATION "${HDSILK_PROBE_ENV_MODS}")
