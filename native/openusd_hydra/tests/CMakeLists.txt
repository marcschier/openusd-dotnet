add_executable(openusd_storm_context_probe context_support_probe.cpp)
target_compile_features(openusd_storm_context_probe PRIVATE cxx_std_17)
target_include_directories(
    openusd_storm_context_probe
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../src)

if(MSVC)
    target_compile_options(
        openusd_storm_context_probe
        PRIVATE /W4 /WX /permissive-)
else()
    target_compile_options(
        openusd_storm_context_probe
        PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

add_test(NAME openusd_storm_context_probe COMMAND openusd_storm_context_probe)

add_executable(openusd_storm_pick_contract_probe pick_contract_probe.cpp)
target_compile_features(openusd_storm_pick_contract_probe PRIVATE cxx_std_17)
target_include_directories(
    openusd_storm_pick_contract_probe
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../src
        ${CMAKE_CURRENT_SOURCE_DIR}/../../include)
target_link_libraries(openusd_storm_pick_contract_probe PRIVATE usd_m)
if(MSVC)
    target_compile_options(
        openusd_storm_pick_contract_probe
        PRIVATE /W4 /WX /permissive-)
else()
    target_compile_options(
        openusd_storm_pick_contract_probe
        PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()
add_test(
    NAME openusd_storm_pick_contract_probe
    COMMAND openusd_storm_pick_contract_probe)
if(WIN32)
    set_tests_properties(
        openusd_storm_pick_contract_probe
        PROPERTIES
            ENVIRONMENT_MODIFICATION
                "PATH=path_list_prepend:${pxr_DIR}/lib;PATH=path_list_prepend:${pxr_DIR}/bin")
elseif(UNIX AND NOT APPLE)
    set_tests_properties(
        openusd_storm_pick_contract_probe
        PROPERTIES
            ENVIRONMENT_MODIFICATION
                "LD_LIBRARY_PATH=path_list_prepend:${pxr_DIR}/lib")
elseif(APPLE)
    set_tests_properties(
        openusd_storm_pick_contract_probe
        PROPERTIES
            ENVIRONMENT_MODIFICATION
                "DYLD_LIBRARY_PATH=path_list_prepend:${pxr_DIR}/lib")
endif()

if(WIN32)
    add_executable(
        openusd_storm_wgl_shared_stage_probe
        storm_wgl_shared_stage_probe.cpp)
    target_compile_features(
        openusd_storm_wgl_shared_stage_probe
        PRIVATE cxx_std_17)
    target_compile_definitions(
        openusd_storm_wgl_shared_stage_probe
        PRIVATE OPENUSD_HYDRA_BUILD)
    target_link_libraries(
        openusd_storm_wgl_shared_stage_probe
        PRIVATE
            openusd_hydra_test
            openusd_dotnet_renderer_test
            usd_m
            opengl32
            gdi32
            user32)
    target_include_directories(
        openusd_storm_wgl_shared_stage_probe
        PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/../../openusd_dotnet/tests)
    target_compile_options(
        openusd_storm_wgl_shared_stage_probe
        PRIVATE /W4 /WX /permissive-)

    set(
        STORM_WGL_PROBE_RUNTIME_DIR
        ${CMAKE_CURRENT_BINARY_DIR}/storm-wgl-runtime)
    set(
        STORM_WGL_PROBE_PLUGIN_DIR
        ${STORM_WGL_PROBE_RUNTIME_DIR}/plugin/usd)
    set(
        STORM_WGL_PROBE_NATIVE_DIR
        ${STORM_WGL_PROBE_RUNTIME_DIR}/bin)
    set(
        STORM_WGL_PROBE_STAMP
        ${STORM_WGL_PROBE_RUNTIME_DIR}/runtime.stamp)
    add_custom_command(
        OUTPUT ${STORM_WGL_PROBE_STAMP}
        COMMAND ${CMAKE_COMMAND} -E make_directory ${STORM_WGL_PROBE_PLUGIN_DIR}
        COMMAND ${CMAKE_COMMAND} -E copy_directory
            ${pxr_DIR}/lib/usd
            ${STORM_WGL_PROBE_PLUGIN_DIR}
        COMMAND ${CMAKE_COMMAND} -E copy_directory
            ${pxr_DIR}/plugin/usd
            ${STORM_WGL_PROBE_PLUGIN_DIR}
        COMMAND ${CMAKE_COMMAND} -E make_directory ${STORM_WGL_PROBE_NATIVE_DIR}
        COMMAND ${CMAKE_COMMAND} -E copy
            $<TARGET_FILE:openusd_hydra>
            ${STORM_WGL_PROBE_NATIVE_DIR}/$<TARGET_FILE_NAME:openusd_hydra>
        COMMAND ${CMAKE_COMMAND} -E copy
            $<TARGET_FILE:openusd_dotnet_renderer_test>
            ${STORM_WGL_PROBE_NATIVE_DIR}/$<TARGET_FILE_NAME:openusd_dotnet_renderer_test>
        COMMAND ${CMAKE_COMMAND} -E touch ${STORM_WGL_PROBE_STAMP}
        DEPENDS openusd_hydra openusd_dotnet_renderer_test
        COMMENT "Assembling Storm WGL shared-stage probe runtime")
    add_custom_target(
        openusd_storm_wgl_shared_stage_probe_runtime
        DEPENDS ${STORM_WGL_PROBE_STAMP})
    add_dependencies(
        openusd_storm_wgl_shared_stage_probe
        openusd_storm_wgl_shared_stage_probe_runtime)

    get_filename_component(
        STORM_WGL_PROBE_STAGE
        "${CMAKE_CURRENT_SOURCE_DIR}/../../../test-assets/minimal.usda"
        ABSOLUTE)
    add_test(
        NAME openusd_storm_wgl_shared_stage_probe_legacy
        COMMAND
            openusd_storm_wgl_shared_stage_probe
            ${STORM_WGL_PROBE_PLUGIN_DIR}
            ${STORM_WGL_PROBE_STAGE})
    add_test(
        NAME openusd_storm_wgl_shared_stage_probe_scene_index
        COMMAND
            openusd_storm_wgl_shared_stage_probe
            ${STORM_WGL_PROBE_PLUGIN_DIR}
            ${STORM_WGL_PROBE_STAGE})
    set_tests_properties(
        openusd_storm_wgl_shared_stage_probe_legacy
        PROPERTIES
            SKIP_RETURN_CODE 125
            ENVIRONMENT_MODIFICATION
                "PATH=path_list_prepend:${STORM_WGL_PROBE_NATIVE_DIR};PATH=path_list_prepend:${pxr_DIR}/lib;PATH=path_list_prepend:${pxr_DIR}/bin;USDIMAGINGGL_ENGINE_ENABLE_SCENE_INDEX=set:0;USDIMAGINGGL_ENGINE_ENABLE_SCENE_INDEX_DEPRECATION_WARNING=set:0")
    set_tests_properties(
        openusd_storm_wgl_shared_stage_probe_scene_index
        PROPERTIES
            SKIP_RETURN_CODE 125
            ENVIRONMENT_MODIFICATION
                "PATH=path_list_prepend:${STORM_WGL_PROBE_NATIVE_DIR};PATH=path_list_prepend:${pxr_DIR}/lib;PATH=path_list_prepend:${pxr_DIR}/bin;USDIMAGINGGL_ENGINE_ENABLE_SCENE_INDEX=set:1")
endif()
