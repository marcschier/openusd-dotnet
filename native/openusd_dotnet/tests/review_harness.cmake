# Copyright (c) marcschier. Licensed under the MIT License.
# Include from an owned standalone CMakeLists.txt with SDK_ROOT and REPO_ROOT.
cmake_minimum_required(VERSION 3.28)
project(OpenUsdPortableReviewValidation LANGUAGES C CXX)

file(TO_CMAKE_PATH "${SDK_ROOT}" SDK_ROOT)
file(TO_CMAKE_PATH "${REPO_ROOT}" REPO_ROOT)
set(CMAKE_PREFIX_PATH "${SDK_ROOT}")
find_package(OpenGL REQUIRED)
find_package(Ptex CONFIG REQUIRED)
add_library(Vulkan::Vulkan INTERFACE IMPORTED)
add_library(Vulkan::shaderc_combined INTERFACE IMPORTED)
find_package(pxr CONFIG REQUIRED)
find_package(OpenColorIO CONFIG REQUIRED)
get_property(importedTargets DIRECTORY PROPERTY IMPORTED_TARGETS)
foreach(target IN LISTS importedTargets)
    foreach(property INTERFACE_INCLUDE_DIRECTORIES INTERFACE_SYSTEM_INCLUDE_DIRECTORIES INTERFACE_LINK_LIBRARIES)
        get_target_property(value "${target}" "${property}")
        if(value)
            string(REPLACE "D:/a/openusd-dotnet/openusd-dotnet/native/install/win-x64"
                "${SDK_ROOT}" value "${value}")
            set_target_properties("${target}" PROPERTIES "${property}" "${value}")
        endif()
    endforeach()
endforeach()
set(OPENUSD_BUILD_NATIVE_TESTS OFF)
set(OPENUSD_BUILD_NATIVE_FUZZERS OFF)
add_subdirectory("${REPO_ROOT}/native/openusd_dotnet" shim)
option(PORTABLE_REVIEW_TEST_HOOKS "Enable isolated native failure injection" OFF)
if(PORTABLE_REVIEW_TEST_HOOKS)
    target_compile_definitions(openusd_dotnet PRIVATE OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
endif()

add_executable(openusd_review_sdk_probe
    "${REPO_ROOT}/native/openusd_dotnet/tests/review_sdk_probe.cpp")
target_link_libraries(openusd_review_sdk_probe PRIVATE usd_m)
add_executable(openusd_review_probe
    "${REPO_ROOT}/native/openusd_dotnet/tests/review_probe.cpp"
    "${REPO_ROOT}/native/openusd_dotnet/tests/review_cases.cpp"
    "${REPO_ROOT}/native/openusd_dotnet/tests/review_typed_cases.cpp"
    "${REPO_ROOT}/native/openusd_dotnet/tests/review_bounds_cases.cpp"
    "${REPO_ROOT}/native/openusd_dotnet/tests/review_negative_cases.cpp")
target_link_libraries(openusd_review_probe PRIVATE openusd_dotnet usd_m)
if(WIN32)
    target_link_libraries(openusd_review_probe PRIVATE bcrypt)
endif()
add_executable(openusd_layer_edit_probe
    "${REPO_ROOT}/native/openusd_dotnet/tests/layer_edit_probe.cpp"
    "${REPO_ROOT}/native/openusd_dotnet/tests/layer_edit_cases.cpp")
target_include_directories(openusd_layer_edit_probe PRIVATE "${REPO_ROOT}/native/openusd_dotnet/private")
target_link_libraries(openusd_layer_edit_probe PRIVATE openusd_dotnet usd_m)

enable_testing()
foreach(probe openusd_review_sdk_probe openusd_review_probe openusd_layer_edit_probe)
    target_compile_features(${probe} PRIVATE cxx_std_17)
    if(MSVC)
        target_compile_options(${probe} PRIVATE /W4 /WX /permissive-)
    else()
        target_compile_options(${probe} PRIVATE -Wall -Wextra -Wpedantic -Werror)
    endif()
    if(PORTABLE_REVIEW_TEST_HOOKS)
        target_compile_definitions(${probe} PRIVATE OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
    endif()
    add_test(NAME ${probe} COMMAND ${probe} "${SDK_ROOT}/lib/usd" "${CMAKE_CURRENT_BINARY_DIR}")
    set_tests_properties(${probe} PROPERTIES ENVIRONMENT_MODIFICATION
        "PATH=path_list_prepend:${SDK_ROOT}/bin;PATH=path_list_prepend:${SDK_ROOT}/lib;PATH=path_list_prepend:$<TARGET_FILE_DIR:openusd_dotnet>")
endforeach()
include("${REPO_ROOT}/native/openusd_dotnet/tests/review_cold_inspection_probe.cmake")
openusd_add_cold_inspection_probe("${SDK_ROOT}")
add_test(NAME openusd_review_process_write COMMAND openusd_review_probe
    "${SDK_ROOT}/lib/usd" "${CMAKE_CURRENT_BINARY_DIR}" write)
add_test(NAME openusd_review_process_read COMMAND openusd_review_probe
    "${SDK_ROOT}/lib/usd" "${CMAKE_CURRENT_BINARY_DIR}" read)
set_tests_properties(openusd_review_process_write PROPERTIES FIXTURES_SETUP portable_document)
set_tests_properties(openusd_review_process_read PROPERTIES FIXTURES_REQUIRED portable_document)
set_tests_properties(openusd_review_process_write openusd_review_process_read PROPERTIES ENVIRONMENT_MODIFICATION
    "PATH=path_list_prepend:${SDK_ROOT}/bin;PATH=path_list_prepend:${SDK_ROOT}/lib;PATH=path_list_prepend:$<TARGET_FILE_DIR:openusd_dotnet>")
