cmake_minimum_required(VERSION 3.28)
project(OpenUsdPublicStormAovValidation LANGUAGES C CXX)

if(NOT WIN32)
    message(FATAL_ERROR "This staged executable validation graph requires Windows WGL.")
endif()
if(NOT OPENUSD_AOV_SDK_ROOT OR NOT OPENUSD_AOV_BASE_RUNTIME)
    message(FATAL_ERROR "Explicit matched SDK/runtime roots are required.")
endif()
get_filename_component(OPENUSD_AOV_REPO_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../../../.." ABSOLUTE)
list(PREPEND CMAKE_PREFIX_PATH "${OPENUSD_AOV_SDK_ROOT}")
find_package(OpenGL REQUIRED)
add_library(Vulkan::Vulkan INTERFACE IMPORTED)
add_library(Vulkan::shaderc_combined INTERFACE IMPORTED)
find_package(Ptex CONFIG REQUIRED)
find_package(pxr CONFIG REQUIRED)
find_package(OpenColorIO CONFIG REQUIRED)
add_library(openusd_dotnet SHARED IMPORTED)
set_target_properties(openusd_dotnet PROPERTIES
    IMPORTED_LOCATION "${OPENUSD_AOV_BASE_RUNTIME}/bin/openusd_dotnet.dll"
    IMPORTED_IMPLIB "${OPENUSD_AOV_BASE_RUNTIME}/lib/openusd_dotnet.lib")
target_include_directories(openusd_dotnet INTERFACE
    "${OPENUSD_AOV_REPO_ROOT}/native/openusd_dotnet/include")
set(OPENUSD_BUILD_NATIVE_TESTS OFF CACHE BOOL "" FORCE)
set(OPENUSD_HYDRA_BUILD_TESTS OFF CACHE BOOL "" FORCE)
add_subdirectory("${OPENUSD_AOV_REPO_ROOT}/native/openusd_hydra" "${CMAKE_CURRENT_BINARY_DIR}/hydra")
add_subdirectory("${OPENUSD_AOV_REPO_ROOT}/native/openusd_storm_child" "${CMAKE_CURRENT_BINARY_DIR}/child")

enable_testing()
add_executable(openusd_storm_aov_public_abi_probe ../storm_aov_public_abi_probe.c)
target_compile_features(openusd_storm_aov_public_abi_probe PRIVATE c_std_11)
target_compile_options(openusd_storm_aov_public_abi_probe PRIVATE /W4 /WX)
target_link_libraries(openusd_storm_aov_public_abi_probe
    PRIVATE openusd_hydra openusd_dotnet opengl32 gdi32 user32)
add_test(NAME openusd_storm_aov_public_abi_probe COMMAND openusd_storm_aov_public_abi_probe
    "${OPENUSD_AOV_BASE_RUNTIME}/plugin/usd"
    "${CMAKE_CURRENT_SOURCE_DIR}/../storm_aov_planes.usda")
set_tests_properties(openusd_storm_aov_public_abi_probe PROPERTIES
    ENVIRONMENT_MODIFICATION
        "PATH=path_list_prepend:${OPENUSD_AOV_BASE_RUNTIME}/bin;PATH=path_list_prepend:${OPENUSD_AOV_BASE_RUNTIME}/lib;PATH=path_list_prepend:$<TARGET_FILE_DIR:openusd_hydra>")

add_executable(openusd_storm_aov_child_preservation_probe
    "${OPENUSD_AOV_REPO_ROOT}/native/openusd_storm_child/tests/storm_child_probe.cpp")
target_compile_features(openusd_storm_aov_child_preservation_probe PRIVATE cxx_std_17)
target_compile_options(openusd_storm_aov_child_preservation_probe PRIVATE /W4 /WX /permissive- /EHsc)
target_include_directories(openusd_storm_aov_child_preservation_probe
    PRIVATE "${OPENUSD_AOV_REPO_ROOT}/native/openusd_storm_child/src")
target_link_libraries(openusd_storm_aov_child_preservation_probe
    PRIVATE openusd_storm_child openusd_hydra openusd_dotnet user32)
add_test(NAME openusd_storm_aov_child_preservation_probe
    COMMAND openusd_storm_aov_child_preservation_probe
        "${OPENUSD_AOV_BASE_RUNTIME}/plugin/usd"
        "${OPENUSD_AOV_REPO_ROOT}/test-assets/minimal.usda"
        --render-only)
set_tests_properties(openusd_storm_aov_child_preservation_probe PROPERTIES
    TIMEOUT 180
    ENVIRONMENT_MODIFICATION
        "PATH=path_list_prepend:${OPENUSD_AOV_BASE_RUNTIME}/bin;PATH=path_list_prepend:${OPENUSD_AOV_BASE_RUNTIME}/lib;PATH=path_list_prepend:$<TARGET_FILE_DIR:openusd_hydra>;PATH=path_list_prepend:$<TARGET_FILE_DIR:openusd_storm_child>")
