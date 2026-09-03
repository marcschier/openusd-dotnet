cmake_minimum_required(VERSION 3.28)

project(OpenUsdNative LANGUAGES C CXX)

include(GNUInstallDirs)
install(
    FILES
        include/openusd_render_camera.h
        include/openusd_render_lighting.h
        include/openusd_render_pick.h
        include/openusd_render_physics.h
        include/openusd_mdl.h
    DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})

option(OPENUSD_ENABLE_ASAN "Enable AddressSanitizer for project native targets" OFF)
option(OPENUSD_ENABLE_UBSAN "Enable UndefinedBehaviorSanitizer for project native targets" OFF)
option(OPENUSD_ENABLE_TSAN "Enable ThreadSanitizer for project native targets" OFF)
option(OPENUSD_BUILD_NATIVE_TESTS "Build native ABI probes" ON)
option(OPENUSD_BUILD_NATIVE_FUZZERS "Build Linux Clang libFuzzer targets" OFF)
option(OPENUSD_WITH_CESIUM "Build the quarantined cesium-native C ABI shim" OFF)
option(OPENUSD_WITH_PHYSX "Resolve the quarantined PhysX simulation SDK" OFF)
# The optional MDL material adapter is off by default and is never part of a
# base package. See native/openusd_mdl/README.md and eng/mdl.lock.json for what
# it does and does not contain.
option(OPENUSD_WITH_MDL "Build the optional openusd_mdl material adapter" OFF)

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
else()
    # OpenUSD's exported hgiVulkan target names Vulkan::Vulkan and
    # Vulkan::shaderc_combined in its interface whenever the locked OpenUSD build
    # enabled Vulkan, which the viewer-standard profile does on Windows and Linux.
    # find_package(pxr) below imports that target regardless of what this build
    # links, so generation fails on a dangling target reference unless the names
    # exist.
    #
    # The placeholders are deliberately unconditional on which optional shim is
    # enabled. This branch used to require OPENUSD_WITH_CESIUM, which silently
    # made "no Vulkan" mean "no Vulkan, but only for Cesium": turning Vulkan off
    # for the physics presets then failed configure on a target that has nothing
    # to do with physics. The condition that matters is whether the names are
    # missing, not why they are missing.
    #
    # Interface libraries with no properties are correct here rather than a
    # convenience: nothing in this build links Vulkan when the option is off, so
    # the placeholders must contribute no include directory, no library, and no
    # compile definition. A real loader is resolved only by the ON branch.
    if(NOT TARGET Vulkan::Vulkan)
        add_library(Vulkan::Vulkan INTERFACE IMPORTED)
    endif()
    if(NOT TARGET Vulkan::shaderc_combined)
        add_library(Vulkan::shaderc_combined INTERFACE IMPORTED)
    endif()
endif()
if(UNIX AND NOT APPLE)
    find_package(X11 REQUIRED)
endif()
find_package(Ptex CONFIG QUIET)
find_package(pxr CONFIG REQUIRED)
find_package(OpenColorIO CONFIG REQUIRED)
if(OPENUSD_WITH_CESIUM)
    find_package(cesium-native CONFIG REQUIRED)
endif()
if(OPENUSD_WITH_PHYSX)
    find_package(unofficial-omniverse-physx-sdk CONFIG QUIET)
    if(NOT TARGET unofficial::omniverse-physx-sdk::sdk)
        message(FATAL_ERROR "OPENUSD_WITH_PHYSX requires the vcpkg PhysX package config.")
    endif()
endif()

add_subdirectory(openusd_dotnet)
add_subdirectory(openusd_hydra)
if(WIN32 OR UNIX)
    add_subdirectory(openusd_storm_child)
endif()
if(OPENUSD_WITH_MDL)
    # Configured before hdSilk so the hdSilk probe can name the built adapter.
    add_subdirectory(openusd_mdl)
endif()
add_subdirectory(hdSilk)
if(OPENUSD_WITH_CESIUM)
    add_subdirectory(openusd_cesium)
endif()
if(OPENUSD_WITH_PHYSX)
    add_subdirectory(openusd_physx)
endif()

if(OPENUSD_BUILD_NATIVE_TESTS)
    enable_testing()
    add_subdirectory(tests)
    if(OPENUSD_WITH_CESIUM)
        add_subdirectory(openusd_cesium_probe)
    endif()
    # The physics probe directory also holds the PhysX free page contract probe,
    # so it is always added and guards the targets that need the simulation SDK.
    add_subdirectory(openusd_physx_probe)
endif()
