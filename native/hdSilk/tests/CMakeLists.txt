add_executable(
    hdsilk_probe
    hdsilk_probe.cpp
    ../src/curveWidths.cpp
    ../src/instanceLinking.cpp
    ../src/material.cpp
    ../src/materialXBridge.cpp
    ../src/mdlAdapter.cpp
    ../src/sceneState.cpp)
target_compile_features(hdsilk_probe PRIVATE cxx_std_17)
target_link_libraries(
    hdsilk_probe
    PRIVATE
        openusd_hdsilk_test
        openusd_dotnet_renderer_test
        usd_m
        MaterialXFormat
        ${OPENUSD_HDSILK_MATERIALX_VULKAN_LIBS})
if(OPENUSD_HDSILK_MATERIALX_VULKAN_LIBS)
    target_compile_definitions(
        hdsilk_probe
        PRIVATE OPENUSD_HDSILK_WITH_MATERIALX_VULKAN)
endif()

# Two stub libraries that exist only to exercise the MDL adapter loader.
#
# They are staged into a private directory that is on no search path: not the
# probe's own directory, not the runtime plugin directory, and not the working
# directory. Loading the ABI stub from there therefore proves that an absolute
# path is honoured *and* that the stub's own dependency resolves from the
# directory the stub was loaded from rather than from a process-wide search.
add_library(openusd_mdl_stub_support SHARED mdl_stub_support.cpp mdl_stub_support.h)
target_compile_features(openusd_mdl_stub_support PRIVATE cxx_std_17)
target_compile_definitions(
    openusd_mdl_stub_support PRIVATE HDSILK_MDL_STUB_SUPPORT_BUILD)
target_include_directories(
    openusd_mdl_stub_support PUBLIC ${CMAKE_CURRENT_SOURCE_DIR})

add_library(openusd_mdl_abi_stub SHARED mdl_abi_stub.cpp)
target_compile_features(openusd_mdl_abi_stub PRIVATE cxx_std_17)
target_compile_definitions(openusd_mdl_abi_stub PRIVATE OPENUSD_MDL_BUILD)
target_include_directories(
    openusd_mdl_abi_stub PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/../../include)
target_link_libraries(openusd_mdl_abi_stub PRIVATE openusd_mdl_stub_support)
if(NOT WIN32)
    # The Unix half of the same claim: the stub finds its dependency through its
    # own run path rather than through a process-wide search.
    if(APPLE)
        set_target_properties(
            openusd_mdl_abi_stub PROPERTIES BUILD_RPATH "@loader_path")
    else()
        set_target_properties(
            openusd_mdl_abi_stub PROPERTIES BUILD_RPATH "$ORIGIN")
    endif()
endif()

set(HDSILK_MDL_PRIVATE_DIR ${CMAKE_CURRENT_BINARY_DIR}/mdl-private)
set(HDSILK_MDL_PRIVATE_STAMP ${HDSILK_MDL_PRIVATE_DIR}/stubs.stamp)
add_custom_command(
    OUTPUT ${HDSILK_MDL_PRIVATE_STAMP}
    COMMAND ${CMAKE_COMMAND} -E make_directory ${HDSILK_MDL_PRIVATE_DIR}
    COMMAND ${CMAKE_COMMAND} -E copy
        $<TARGET_FILE:openusd_mdl_abi_stub>
        ${HDSILK_MDL_PRIVATE_DIR}/$<TARGET_FILE_NAME:openusd_mdl_abi_stub>
    COMMAND ${CMAKE_COMMAND} -E copy
        $<TARGET_FILE:openusd_mdl_stub_support>
        ${HDSILK_MDL_PRIVATE_DIR}/$<TARGET_FILE_NAME:openusd_mdl_stub_support>
    COMMAND ${CMAKE_COMMAND} -E touch ${HDSILK_MDL_PRIVATE_STAMP}
    DEPENDS openusd_mdl_abi_stub openusd_mdl_stub_support
    COMMENT "Staging MDL loader stub libraries in a private directory")
add_custom_target(hdsilk_mdl_private_stubs DEPENDS ${HDSILK_MDL_PRIVATE_STAMP})
add_dependencies(hdsilk_probe hdsilk_mdl_private_stubs)
target_compile_definitions(
    hdsilk_probe
    PRIVATE
        "HDSILK_PROBE_MDL_ABI_STUB=\"${HDSILK_MDL_PRIVATE_DIR}/$<TARGET_FILE_NAME:openusd_mdl_abi_stub>\""
        "HDSILK_PROBE_MDL_DECOY_DIR=\"${HDSILK_MDL_PRIVATE_DIR}\""
        "HDSILK_PROBE_MDL_LIBRARY_NAME=\"$<TARGET_FILE_PREFIX:openusd_mdl_abi_stub>openusd_mdl$<TARGET_FILE_SUFFIX:openusd_mdl_abi_stub>\"")

# The probe proves both adapter states. Without OPENUSD_WITH_MDL it proves only
# the state every base package ships in -- no adapter, MDL-only materials
# reported rather than shaded -- and the distillation checks are not compiled,
# because asserting them without an adapter would assert nothing.
if(OPENUSD_WITH_MDL)
    add_dependencies(hdsilk_probe openusd_mdl)
    target_compile_definitions(
        hdsilk_probe
        PRIVATE "HDSILK_PROBE_MDL_ADAPTER=\"$<TARGET_FILE:openusd_mdl>\"")
endif()

# The SDK-backed end-to-end check compiles only when an SDK-backed adapter
# exists. It needs a module directory as well: the whole claim is that a value
# the stage did not author came out of a compiled module, and without a module
# there is nothing to compile.
if(TARGET openusd_mdl_sdk)
    get_filename_component(
        HDSILK_PROBE_MDL_MODULE_DIR
        "${CMAKE_CURRENT_SOURCE_DIR}/../../../test-assets/mdl"
        ABSOLUTE)
    add_dependencies(hdsilk_probe openusd_mdl_sdk)
    target_compile_definitions(
        hdsilk_probe
        PRIVATE
            "HDSILK_PROBE_MDL_SDK_ADAPTER=\"$<TARGET_FILE:openusd_mdl_sdk>\""
            "HDSILK_PROBE_MDL_MODULE_DIR=\"${HDSILK_PROBE_MDL_MODULE_DIR}\"")
    if(OPENUSD_MDL_SDK_RUNTIME_DIR)
        target_compile_definitions(
            hdsilk_probe
            PRIVATE
                "HDSILK_PROBE_MDL_SDK_RUNTIME_DIR=\"${OPENUSD_MDL_SDK_RUNTIME_DIR}\"")
    endif()
endif()
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

# The subdivision probe is a separate executable rather than another case inside
# hdsilk_probe: every claim it makes is closed-form arithmetic about a control
# cage, so it needs neither the shared probe's material fixtures nor its MDL
# stubs, and keeping it separate means a subdivision regression names itself.
add_executable(hdsilk_subdivision_probe hdsilk_subdivision_probe.cpp)
target_compile_features(hdsilk_subdivision_probe PRIVATE cxx_std_17)
target_link_libraries(hdsilk_subdivision_probe PRIVATE openusd_hdsilk_test)
target_include_directories(
    hdsilk_subdivision_probe
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}
        ${CMAKE_CURRENT_SOURCE_DIR}/../src)
if(MSVC)
    target_compile_options(hdsilk_subdivision_probe PRIVATE /W4 /WX /permissive-)
else()
    target_compile_options(
        hdsilk_subdivision_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()
add_dependencies(hdsilk_subdivision_probe hdsilk_probe_runtime)

get_filename_component(
    HDSILK_SUBDIVISION_PROBE_STAGE
    "${CMAKE_CURRENT_SOURCE_DIR}/../../../test-assets/hdsilk-subdivision-probe.usda"
    ABSOLUTE)
add_test(NAME hdsilk_subdivision_probe
    COMMAND
        hdsilk_subdivision_probe
        ${HDSILK_PROBE_PLUGIN_DIR}
        ${HDSILK_SUBDIVISION_PROBE_STAGE})
set_tests_properties(hdsilk_subdivision_probe
    PROPERTIES ENVIRONMENT_MODIFICATION "${HDSILK_PROBE_ENV_MODS}")
