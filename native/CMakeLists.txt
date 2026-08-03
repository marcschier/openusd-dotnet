cmake_minimum_required(VERSION 3.28)

project(OpenUsdNative LANGUAGES C CXX)

include(GNUInstallDirs)
install(
    FILES
        include/openusd_render_camera.h
        include/openusd_render_lighting.h
        include/openusd_render_pick.h
    DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})

option(OPENUSD_ENABLE_ASAN "Enable AddressSanitizer for project native targets" OFF)
option(OPENUSD_ENABLE_UBSAN "Enable UndefinedBehaviorSanitizer for project native targets" OFF)
option(OPENUSD_ENABLE_TSAN "Enable ThreadSanitizer for project native targets" OFF)
option(OPENUSD_BUILD_NATIVE_TESTS "Build native ABI probes" ON)
option(OPENUSD_BUILD_NATIVE_FUZZERS "Build Linux Clang libFuzzer targets" OFF)

if(OPENUSD_BUILD_NATIVE_FUZZERS)
    if(NOT CMAKE_SYSTEM_NAME STREQUAL "Linux" OR
       NOT CMAKE_CXX_COMPILER_ID STREQUAL "Clang")
        message(
            FATAL_ERROR
            "OPENUSD_BUILD_NATIVE_FUZZERS requires Linux with Clang and libFuzzer.")
    endif()
    if(OPENUSD_ENABLE_TSAN)
        message(FATAL_ERROR "Native fuzzers cannot be combined with ThreadSanitizer.")
    endif()
endif()

if(OPENUSD_ENABLE_TSAN AND (OPENUSD_ENABLE_ASAN OR OPENUSD_ENABLE_UBSAN))
    message(FATAL_ERROR "ThreadSanitizer must be configured separately from ASan/UBSan.")
endif()

add_library(openusd_native_sanitizers INTERFACE)
if(NOT MSVC)
    if(OPENUSD_ENABLE_ASAN)
        target_compile_options(openusd_native_sanitizers INTERFACE -fsanitize=address -fno-omit-frame-pointer)
        target_link_options(openusd_native_sanitizers INTERFACE -fsanitize=address)
    endif()
    if(OPENUSD_ENABLE_UBSAN)
        target_compile_options(openusd_native_sanitizers INTERFACE -fsanitize=undefined -fno-omit-frame-pointer)
        target_link_options(openusd_native_sanitizers INTERFACE -fsanitize=undefined)
    endif()
    if(OPENUSD_ENABLE_TSAN)
        target_compile_options(openusd_native_sanitizers INTERFACE -fsanitize=thread -fno-omit-frame-pointer)
        target_link_options(openusd_native_sanitizers INTERFACE -fsanitize=thread)
    endif()
elseif(OPENUSD_ENABLE_ASAN OR OPENUSD_ENABLE_UBSAN OR OPENUSD_ENABLE_TSAN)
    message(FATAL_ERROR "The project sanitizer options currently support GCC and Clang builds only.")
endif()

# OpenUSD v26.05 includes GCC backward headers that intentionally emit #warning.
if(CMAKE_CXX_COMPILER_ID STREQUAL "GNU")
    add_compile_options(-Wno-cpp)
endif()

find_package(OpenGL REQUIRED)
option(OPENUSD_WITH_VULKAN "Resolve Vulkan targets required by the OpenUSD build" OFF)
if(OPENUSD_WITH_VULKAN)
    find_package(Vulkan REQUIRED COMPONENTS shaderc_combined)
endif()
if(UNIX AND NOT APPLE)
    find_package(X11 REQUIRED)
endif()
find_package(Ptex CONFIG REQUIRED)
find_package(pxr CONFIG REQUIRED)

add_subdirectory(openusd_dotnet)
add_subdirectory(openusd_hydra)
if(WIN32 OR UNIX)
    add_subdirectory(openusd_storm_child)
endif()
add_subdirectory(hdSilk)

if(OPENUSD_BUILD_NATIVE_TESTS)
    enable_testing()
    add_subdirectory(tests)
endif()
