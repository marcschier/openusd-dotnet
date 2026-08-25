set(OPENUSD_PHYSX_SOURCES
    include/openusd_physx.h
    include/openusd_physx_world.h
    src/openusd_physx.cpp
    src/openusd_physx_cuda.h
    src/openusd_physx_cuda.cpp
    src/openusd_physx_events.h
    src/openusd_physx_events.cpp
    src/openusd_physx_gpu.h
    src/openusd_physx_gpu.cpp
    src/openusd_physx_page.h
    src/openusd_physx_page.cpp
    src/openusd_physx_runtime.h
    src/openusd_physx_runtime.cpp
    src/openusd_physx_scene.cpp
    src/openusd_physx_support.h
    src/openusd_physx_support.cpp
    src/openusd_physx_translate.h
    src/openusd_physx_translate.cpp
    src/openusd_physx_vehicle.h
    src/openusd_physx_vehicle.cpp
    src/openusd_physx_world.cpp)

add_library(openusd_physx SHARED ${OPENUSD_PHYSX_SOURCES})
target_compile_features(openusd_physx PRIVATE cxx_std_17)
target_compile_definitions(openusd_physx PRIVATE OPENUSD_PHYSX_BUILD PX_PHYSX_STATIC_LIB)
target_include_directories(openusd_physx
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src)
# The pinned port's CMake config appends PhysXVehicle2 to its aggregate SDK target only inside an
# `if(WIN32)`, while these sources use `physx::vehicle2` unconditionally. On every other platform
# the aggregate therefore describes a solver with no vehicle implementation in it, and the build
# ends in a wall of undefined references far from the cause -- or, worse, silently drops the
# vehicle capability if the sources are ever made conditional without the packaging following.
#
# The library is located explicitly here, out of the same install directory the aggregate target
# was resolved from, and configure fails when it is absent. Failing closed is deliberate: a
# published package must never claim operational vehicles it cannot run. It is listed before the
# aggregate because GNU ld resolves static archives in command-line order and PhysXVehicle2 draws
# on PhysXFoundation and PhysXCommon, which the aggregate supplies. Naming the aggregate twice
# would not reorder it: CMake keeps the first position it saw, so the whole link line is stated
# once per platform instead.
if(WIN32)
    target_link_libraries(openusd_physx PRIVATE unofficial::omniverse-physx-sdk::sdk usd_m)
else()
    get_target_property(openusd_physx_sdk_library
        unofficial::omniverse-physx-sdk::sdk IMPORTED_LOCATION_RELEASE)
    if(NOT openusd_physx_sdk_library)
        message(FATAL_ERROR
            "The PhysX SDK target has no release import location, so the directory holding "
            "PhysXVehicle2 cannot be derived.")
    endif()
    get_filename_component(openusd_physx_library_dir "${openusd_physx_sdk_library}" DIRECTORY)
    find_library(OPENUSD_PHYSX_VEHICLE2_LIBRARY
        NAMES PhysXVehicle2_static_64 PhysXVehicle2_64
        PATHS "${openusd_physx_library_dir}"
        NO_DEFAULT_PATH)
    if(NOT OPENUSD_PHYSX_VEHICLE2_LIBRARY)
        message(FATAL_ERROR
            "The pinned PhysX install at '${openusd_physx_library_dir}' provides no PhysXVehicle2 "
            "library. openusd_physx compiles physx::vehicle2 unconditionally, so the vehicle "
            "capability cannot be honest without it. Either restore the library in the install or "
            "remove the vehicle sources and the vehicle capability together, and correct the "
            "package documentation to match.")
    endif()
    get_target_property(OPENUSD_PHYSX_EXTENSIONS_LIBRARY
        unofficial::omniverse-physx-sdk::PhysXExtensions IMPORTED_LOCATION_RELEASE)
    if(NOT OPENUSD_PHYSX_EXTENSIONS_LIBRARY)
        message(FATAL_ERROR
            "The PhysX Extensions target has no release import location. Linux needs that archive "
            "after the aggregate SDK to resolve the generated geometry and metadata helpers.")
    endif()
    message(STATUS "openusd_physx: PhysXVehicle2 -> ${OPENUSD_PHYSX_VEHICLE2_LIBRARY}")
    message(STATUS "openusd_physx: trailing PhysXExtensions -> ${OPENUSD_PHYSX_EXTENSIONS_LIBRARY}")
    if(CMAKE_SYSTEM_NAME STREQUAL "Linux")
        string(CONCAT OPENUSD_PHYSX_LINUX_LINK_GROUP
            "$<LINK_GROUP:RESCAN,"
            "${OPENUSD_PHYSX_VEHICLE2_LIBRARY},"
            "unofficial::omniverse-physx-sdk::sdk,"
            "unofficial::omniverse-physx-sdk::PhysXExtensions,"
            "unofficial::omniverse-physx-sdk::PhysXPvdSDK,"
            "unofficial::omniverse-physx-sdk::PhysXCharacterKinematic,"
            "unofficial::omniverse-physx-sdk::PhysXCooking,"
            "unofficial::omniverse-physx-sdk::PhysXCommon,"
            "unofficial::omniverse-physx-sdk::PhysXFoundation,"
            "unofficial::omniverse-physx-sdk::PhysXVehicle>")
        target_link_libraries(openusd_physx
            PRIVATE
                "${OPENUSD_PHYSX_LINUX_LINK_GROUP}"
                usd_m)
        target_link_options(openusd_physx PRIVATE -Wl,-z,defs)
    else()
        target_link_libraries(openusd_physx
            PRIVATE
                "${OPENUSD_PHYSX_VEHICLE2_LIBRARY}"
                unofficial::omniverse-physx-sdk::sdk
                "${OPENUSD_PHYSX_EXTENSIONS_LIBRARY}"
                usd_m)
    endif()
endif()
if(TARGET openusd_native_sanitizers)
    target_link_libraries(openusd_physx PRIVATE openusd_native_sanitizers)
endif()
if(MSVC)
    target_compile_options(openusd_physx PRIVATE /W4 /WX /permissive- /EHsc)
else()
    target_compile_options(openusd_physx PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

include(GNUInstallDirs)

# The CUDA acceleration module is loaded late, by name, from the directory the
# runtime itself lives in. Staging it next to the built library is what makes the
# optional GPU domains reachable on a machine that has a device; a machine that
# has none simply fails the late load and every GPU object is skipped one by one.
# The copy is conditional because the module only exists for the platforms the
# simulation SDK builds GPU support for.
#
# Nothing here is redistributable. These modules are packman blobs the vcpkg port
# downloads under NVIDIA proprietary terms rather than artifacts built from the
# BSD-3-Clause sources, so they are staged for local runs and CI probes only and
# no OpenUsd package ever contains one. A licensed user places the same files
# beside the deployed runtime to enable GPU domains.
#
# The install destination follows the shim rather than the module kind: a Windows
# build installs openusd_physx as a runtime artifact into the binary directory,
# while a Linux build installs the same shim as a library artifact into the
# library directory. Installing the module into the other one would leave it
# somewhere the loader never looks beside the runtime that needs it.
if(WIN32)
    set(openusd_physx_gpu_destination ${CMAKE_INSTALL_BINDIR})
else()
    set(openusd_physx_gpu_destination ${CMAKE_INSTALL_LIBDIR})
endif()
foreach(openusd_physx_gpu_target
        unofficial::omniverse-physx-sdk::gpu-library
        unofficial::omniverse-physx-sdk::gpu-device-library)
    if(TARGET ${openusd_physx_gpu_target})
        get_target_property(openusd_physx_gpu_module ${openusd_physx_gpu_target} IMPORTED_LOCATION)
        if(openusd_physx_gpu_module AND EXISTS "${openusd_physx_gpu_module}")
            add_custom_command(TARGET openusd_physx POST_BUILD
                COMMAND ${CMAKE_COMMAND} -E copy_if_different
                    "${openusd_physx_gpu_module}" "$<TARGET_FILE_DIR:openusd_physx>"
                COMMENT "Staging the optional PhysX GPU module next to openusd_physx")
            install(FILES "${openusd_physx_gpu_module}"
                DESTINATION ${openusd_physx_gpu_destination})
        endif()
    endif()
endforeach()

install(TARGETS openusd_physx
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
    LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}
    ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR})
install(FILES include/openusd_physx.h include/openusd_physx_world.h
    DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
