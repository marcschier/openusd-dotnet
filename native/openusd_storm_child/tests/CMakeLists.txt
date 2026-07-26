if(WIN32)
    set(STORM_CHILD_PROBE_SOURCE storm_child_probe.cpp)
elseif(UNIX AND NOT APPLE)
    set(STORM_CHILD_PROBE_SOURCE storm_child_probe_linux.cpp)
elseif(APPLE)
    set(STORM_CHILD_PROBE_SOURCE storm_child_probe_macos.mm)
else()
    message(FATAL_ERROR "The Storm child probe is not implemented for this platform.")
endif()
add_executable(openusd_storm_child_probe ${STORM_CHILD_PROBE_SOURCE})
target_compile_features(openusd_storm_child_probe PRIVATE cxx_std_17)
target_include_directories(
    openusd_storm_child_probe
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../include
        ${CMAKE_CURRENT_SOURCE_DIR}/../src
        ${CMAKE_CURRENT_SOURCE_DIR}/../../openusd_dotnet/include
        ${CMAKE_CURRENT_SOURCE_DIR}/../../openusd_hydra/include)
target_link_libraries(
    openusd_storm_child_probe
    PRIVATE
        openusd_storm_child
        openusd_hydra
        openusd_dotnet)
if(WIN32)
    target_link_libraries(openusd_storm_child_probe PRIVATE user32)
    target_compile_options(
        openusd_storm_child_probe
        PRIVATE /W4 /WX /permissive-)
elseif(UNIX AND NOT APPLE)
    if(NOT TARGET X11::Xtst)
        message(FATAL_ERROR "The Linux Storm child probe requires the XTest development library.")
    endif()
    target_link_libraries(
        openusd_storm_child_probe
        PRIVATE OpenGL::GL OpenGL::GLX X11::X11)
    target_link_libraries(openusd_storm_child_probe PRIVATE X11::Xtst)
    target_compile_options(
        openusd_storm_child_probe
        PRIVATE -Wall -Wextra -Wpedantic -Werror)
elseif(APPLE)
    target_link_libraries(
        openusd_storm_child_probe
        PRIVATE
            OpenGL::GL
            ${APPKIT_FRAMEWORK}
            ${FOUNDATION_FRAMEWORK})
    target_compile_options(
        openusd_storm_child_probe
        PRIVATE
            -Wall
            -Wextra
            -Wpedantic
            -Werror
            -Wno-deprecated-declarations)
    set_source_files_properties(
        storm_child_probe_macos.mm
        PROPERTIES COMPILE_FLAGS "-fno-objc-arc")
endif()

set(
    STORM_CHILD_PROBE_RUNTIME_DIR
    ${CMAKE_CURRENT_BINARY_DIR}/storm-child-runtime)
set(
    STORM_CHILD_PROBE_PLUGIN_DIR
    ${STORM_CHILD_PROBE_RUNTIME_DIR}/plugin/usd)
set(
    STORM_CHILD_PROBE_NATIVE_DIR
    ${STORM_CHILD_PROBE_RUNTIME_DIR}/bin)
set(
    STORM_CHILD_PROBE_STAMP
    ${STORM_CHILD_PROBE_RUNTIME_DIR}/runtime.stamp)
add_custom_command(
    OUTPUT ${STORM_CHILD_PROBE_STAMP}
    COMMAND ${CMAKE_COMMAND} -E make_directory ${STORM_CHILD_PROBE_PLUGIN_DIR}
    COMMAND ${CMAKE_COMMAND} -E copy_directory
        ${pxr_DIR}/lib/usd
        ${STORM_CHILD_PROBE_PLUGIN_DIR}
    COMMAND ${CMAKE_COMMAND} -E copy_directory
        ${pxr_DIR}/plugin/usd
        ${STORM_CHILD_PROBE_PLUGIN_DIR}
    COMMAND ${CMAKE_COMMAND} -E make_directory ${STORM_CHILD_PROBE_NATIVE_DIR}
    COMMAND ${CMAKE_COMMAND} -E touch ${STORM_CHILD_PROBE_STAMP}
    COMMENT "Assembling Storm native child probe runtime")
add_custom_target(
    openusd_storm_child_probe_runtime
    DEPENDS ${STORM_CHILD_PROBE_STAMP})
if(UNIX AND NOT APPLE)
    add_custom_target(
        openusd_storm_child_probe_native_runtime
        COMMAND ${CMAKE_COMMAND} -E make_directory ${STORM_CHILD_PROBE_NATIVE_DIR}
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            $<TARGET_FILE:openusd_storm_child>
            ${STORM_CHILD_PROBE_NATIVE_DIR}/$<TARGET_FILE_NAME:openusd_storm_child>
        COMMAND ${CMAKE_COMMAND} -E rm -f
            ${STORM_CHILD_PROBE_NATIVE_DIR}/$<TARGET_SONAME_FILE_NAME:openusd_storm_child>
            ${STORM_CHILD_PROBE_NATIVE_DIR}/$<TARGET_LINKER_FILE_NAME:openusd_storm_child>
        COMMAND ${CMAKE_COMMAND} -E create_symlink
            $<TARGET_FILE_NAME:openusd_storm_child>
            ${STORM_CHILD_PROBE_NATIVE_DIR}/$<TARGET_SONAME_FILE_NAME:openusd_storm_child>
        COMMAND ${CMAKE_COMMAND} -E create_symlink
            $<TARGET_SONAME_FILE_NAME:openusd_storm_child>
            ${STORM_CHILD_PROBE_NATIVE_DIR}/$<TARGET_LINKER_FILE_NAME:openusd_storm_child>
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            $<TARGET_FILE:openusd_hydra>
            ${STORM_CHILD_PROBE_NATIVE_DIR}/$<TARGET_FILE_NAME:openusd_hydra>
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            $<TARGET_FILE:openusd_dotnet>
            ${STORM_CHILD_PROBE_NATIVE_DIR}/$<TARGET_FILE_NAME:openusd_dotnet>
        DEPENDS openusd_storm_child openusd_hydra openusd_dotnet
        COMMENT "Updating versioned Storm native child probe libraries")
else()
    add_custom_target(
        openusd_storm_child_probe_native_runtime
        COMMAND ${CMAKE_COMMAND} -E make_directory ${STORM_CHILD_PROBE_NATIVE_DIR}
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            $<TARGET_FILE:openusd_storm_child>
            ${STORM_CHILD_PROBE_NATIVE_DIR}/$<TARGET_FILE_NAME:openusd_storm_child>
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            $<TARGET_FILE:openusd_hydra>
            ${STORM_CHILD_PROBE_NATIVE_DIR}/$<TARGET_FILE_NAME:openusd_hydra>
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            $<TARGET_FILE:openusd_dotnet>
            ${STORM_CHILD_PROBE_NATIVE_DIR}/$<TARGET_FILE_NAME:openusd_dotnet>
        DEPENDS openusd_storm_child openusd_hydra openusd_dotnet
        COMMENT "Updating Storm native child probe libraries")
endif()
add_dependencies(
    openusd_storm_child_probe
    openusd_storm_child_probe_runtime
    openusd_storm_child_probe_native_runtime)

get_filename_component(
    STORM_CHILD_PROBE_STAGE
    "${CMAKE_CURRENT_SOURCE_DIR}/../../../test-assets/minimal.usda"
    ABSOLUTE)
set(
    STORM_CHILD_PROBE_ARGUMENTS
    ${STORM_CHILD_PROBE_PLUGIN_DIR}
    ${STORM_CHILD_PROBE_STAGE})
if(UNIX AND NOT APPLE)
    list(APPEND STORM_CHILD_PROBE_ARGUMENTS ${STORM_CHILD_PROBE_NATIVE_DIR})
endif()
add_test(
    NAME openusd_storm_child_probe
    COMMAND openusd_storm_child_probe ${STORM_CHILD_PROBE_ARGUMENTS})
if(WIN32)
    set_tests_properties(
        openusd_storm_child_probe
        PROPERTIES
            SKIP_RETURN_CODE 125
            ENVIRONMENT_MODIFICATION
                "PATH=path_list_prepend:${STORM_CHILD_PROBE_NATIVE_DIR};PATH=path_list_prepend:${pxr_DIR}/lib;PATH=path_list_prepend:${pxr_DIR}/bin")
elseif(UNIX AND NOT APPLE)
    set_tests_properties(
        openusd_storm_child_probe
        PROPERTIES
            ENVIRONMENT_MODIFICATION
                "LD_LIBRARY_PATH=path_list_prepend:${STORM_CHILD_PROBE_NATIVE_DIR};LD_LIBRARY_PATH=path_list_prepend:${pxr_DIR}/lib")
elseif(APPLE)
    set_tests_properties(
        openusd_storm_child_probe
        PROPERTIES
            SKIP_RETURN_CODE 125
            ENVIRONMENT_MODIFICATION
                "DYLD_LIBRARY_PATH=path_list_prepend:${STORM_CHILD_PROBE_NATIVE_DIR};DYLD_LIBRARY_PATH=path_list_prepend:${pxr_DIR}/lib")
    install(
        TARGETS openusd_storm_child_probe
        RUNTIME DESTINATION bin)
endif()

# The probe drives a real window, context and render thread, so a lost wakeup
# blocks forever rather than failing. It completes in about two seconds on every
# supported host, so bound it: a hang must surface as a diagnosable timeout
# instead of consuming the whole job budget with no captured output.
set_tests_properties(
    openusd_storm_child_probe
    PROPERTIES TIMEOUT 300)
