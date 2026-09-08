cmake_minimum_required(VERSION 3.28)
project(OpenUsdPrivateStormAov LANGUAGES C CXX)

if(NOT WIN32)
    message(FATAL_ERROR "The private AOV executable proof currently requires Windows WGL.")
endif()
if(NOT OPENUSD_AOV_SDK_ROOT OR NOT OPENUSD_AOV_BASE_RUNTIME)
    message(FATAL_ERROR "Pass explicit read-only SDK and matched frozen runtime roots.")
endif()
get_filename_component(
    OPENUSD_AOV_REPO_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../../../.." ABSOLUTE)
list(PREPEND CMAKE_PREFIX_PATH "${OPENUSD_AOV_SDK_ROOT}")
find_package(OpenGL REQUIRED)
add_library(Vulkan::Vulkan INTERFACE IMPORTED)
add_library(Vulkan::shaderc_combined INTERFACE IMPORTED)
find_package(Ptex CONFIG REQUIRED)
find_package(pxr CONFIG REQUIRED)
find_package(OpenColorIO CONFIG REQUIRED)

add_library(openusd_dotnet SHARED IMPORTED)
set_target_properties(
    openusd_dotnet
    PROPERTIES
        IMPORTED_LOCATION "${OPENUSD_AOV_BASE_RUNTIME}/bin/openusd_dotnet.dll"
        IMPORTED_IMPLIB "${OPENUSD_AOV_BASE_RUNTIME}/lib/openusd_dotnet.lib")
target_include_directories(
    openusd_dotnet INTERFACE "${OPENUSD_AOV_REPO_ROOT}/native/openusd_dotnet/include")
set(OPENUSD_HYDRA_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(OPENUSD_BUILD_NATIVE_TESTS OFF CACHE BOOL "" FORCE)
option(OPENUSD_AOV_BUILD_CANDIDATE "Enable the private AOV contract and probes" ON)
set(OPENUSD_HYDRA_ENABLE_PRIVATE_AOV ${OPENUSD_AOV_BUILD_CANDIDATE} CACHE BOOL "" FORCE)
add_subdirectory(
    "${OPENUSD_AOV_REPO_ROOT}/native/openusd_hydra"
    "${CMAKE_CURRENT_BINARY_DIR}/hydra")
add_subdirectory(
    "${OPENUSD_AOV_REPO_ROOT}/native/openusd_storm_child"
    "${CMAKE_CURRENT_BINARY_DIR}/child")

enable_testing()
add_executable(openusd_storm_aov_sdk_probe ../storm_aov_sdk_probe.cpp)
target_compile_features(openusd_storm_aov_sdk_probe PRIVATE cxx_std_17)
target_compile_options(
    openusd_storm_aov_sdk_probe PRIVATE /W4 /WX /permissive- /EHsc)
target_link_libraries(
    openusd_storm_aov_sdk_probe PRIVATE usd_m opengl32 gdi32 user32)

if(OPENUSD_AOV_BUILD_CANDIDATE)
    add_executable(openusd_storm_aov_candidate_probe ../storm_aov_candidate_probe.cpp)
    target_compile_features(openusd_storm_aov_candidate_probe PRIVATE cxx_std_17)
    target_compile_options(
        openusd_storm_aov_candidate_probe PRIVATE /W4 /WX /permissive- /EHsc)
    target_link_libraries(
        openusd_storm_aov_candidate_probe
        PRIVATE openusd_hydra openusd_dotnet usd_m opengl32 gdi32 user32)

    add_executable(openusd_storm_aov_candidate_abi_probe ../storm_aov_candidate_abi_probe.c)
    target_compile_features(openusd_storm_aov_candidate_abi_probe PRIVATE c_std_11)
    target_compile_options(openusd_storm_aov_candidate_abi_probe PRIVATE /W4 /WX)
    target_link_libraries(
        openusd_storm_aov_candidate_abi_probe PRIVATE openusd_hydra openusd_dotnet)
    add_test(
        NAME openusd_storm_aov_candidate_c11_abi
        COMMAND openusd_storm_aov_candidate_abi_probe)
    set_tests_properties(
        openusd_storm_aov_candidate_c11_abi
        PROPERTIES
            ENVIRONMENT_MODIFICATION
                "PATH=path_list_prepend:${OPENUSD_AOV_BASE_RUNTIME}/bin;PATH=path_list_prepend:${OPENUSD_AOV_BASE_RUNTIME}/lib;PATH=path_list_prepend:$<TARGET_FILE_DIR:openusd_hydra>")
endif()

foreach(contract context_support pick_contract physics_override_contract deformation_override_contract)
    set(probe "openusd_storm_existing_${contract}_probe")
    add_executable(${probe} "../${contract}_probe.cpp")
    target_compile_features(${probe} PRIVATE cxx_std_17)
    target_compile_options(${probe} PRIVATE /W4 /WX /permissive- /EHsc)
    target_include_directories(
        ${probe}
        PRIVATE
            "${CMAKE_CURRENT_SOURCE_DIR}/../../src"
            "${OPENUSD_AOV_REPO_ROOT}/native/include")
    target_link_libraries(${probe} PRIVATE usd_m)
    add_test(NAME ${probe} COMMAND ${probe})
    set_tests_properties(
        ${probe}
        PROPERTIES
            ENVIRONMENT_MODIFICATION
                "PATH=path_list_prepend:${OPENUSD_AOV_BASE_RUNTIME}/bin;PATH=path_list_prepend:${OPENUSD_AOV_BASE_RUNTIME}/lib")
endforeach()

add_executable(
    openusd_storm_existing_child_render_only_probe
    "${OPENUSD_AOV_REPO_ROOT}/native/openusd_storm_child/tests/storm_child_probe.cpp")
target_compile_features(
    openusd_storm_existing_child_render_only_probe PRIVATE cxx_std_17)
target_compile_options(
    openusd_storm_existing_child_render_only_probe PRIVATE /W4 /WX /permissive- /EHsc)
target_include_directories(
    openusd_storm_existing_child_render_only_probe
    PRIVATE "${OPENUSD_AOV_REPO_ROOT}/native/openusd_storm_child/src")
target_link_libraries(
    openusd_storm_existing_child_render_only_probe
    PRIVATE openusd_storm_child openusd_hydra openusd_dotnet user32)
add_test(
    NAME openusd_storm_existing_child_render_only_probe
    COMMAND openusd_storm_existing_child_render_only_probe
        "${OPENUSD_AOV_BASE_RUNTIME}/plugin/usd"
        "${OPENUSD_AOV_REPO_ROOT}/test-assets/minimal.usda"
        --render-only)
set_tests_properties(
    openusd_storm_existing_child_render_only_probe
    PROPERTIES
        TIMEOUT 180
        ENVIRONMENT_MODIFICATION
            "PATH=path_list_prepend:${OPENUSD_AOV_BASE_RUNTIME}/bin;PATH=path_list_prepend:${OPENUSD_AOV_BASE_RUNTIME}/lib;PATH=path_list_prepend:$<TARGET_FILE_DIR:openusd_hydra>;PATH=path_list_prepend:$<TARGET_FILE_DIR:openusd_storm_child>")

foreach(mode legacy scene_index)
    if(mode STREQUAL "legacy")
        set(scene_index 0)
    else()
        set(scene_index 1)
    endif()
    add_test(
        NAME openusd_storm_aov_sdk_${mode}
        COMMAND openusd_storm_aov_sdk_probe
            "${OPENUSD_AOV_BASE_RUNTIME}/plugin/usd"
            "${CMAKE_CURRENT_SOURCE_DIR}/../storm_aov_planes.usda")
    set_tests_properties(
        openusd_storm_aov_sdk_${mode}
        PROPERTIES
            TIMEOUT 180
            ENVIRONMENT_MODIFICATION
                "PATH=path_list_prepend:${OPENUSD_AOV_BASE_RUNTIME}/bin;PATH=path_list_prepend:${OPENUSD_AOV_BASE_RUNTIME}/lib;USDIMAGINGGL_ENGINE_ENABLE_SCENE_INDEX=set:${scene_index};USDIMAGINGGL_ENGINE_ENABLE_SCENE_INDEX_DEPRECATION_WARNING=set:0")
    if(OPENUSD_AOV_BUILD_CANDIDATE)
        add_test(
            NAME openusd_storm_aov_candidate_${mode}
            COMMAND openusd_storm_aov_candidate_probe
                "${OPENUSD_AOV_BASE_RUNTIME}/plugin/usd"
                "${CMAKE_CURRENT_SOURCE_DIR}/../storm_aov_planes.usda")
        set_tests_properties(
            openusd_storm_aov_candidate_${mode}
            PROPERTIES
                TIMEOUT 180
                ENVIRONMENT_MODIFICATION
                    "PATH=path_list_prepend:${OPENUSD_AOV_BASE_RUNTIME}/bin;PATH=path_list_prepend:${OPENUSD_AOV_BASE_RUNTIME}/lib;PATH=path_list_prepend:$<TARGET_FILE_DIR:openusd_hydra>;USDIMAGINGGL_ENGINE_ENABLE_SCENE_INDEX=set:${scene_index};USDIMAGINGGL_ENGINE_ENABLE_SCENE_INDEX_DEPRECATION_WARNING=set:0")
    endif()
endforeach()
