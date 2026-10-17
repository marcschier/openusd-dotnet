# Copyright (c) marcschier. Licensed under the MIT License.
if(NOT CMAKE_SYSTEM_NAME STREQUAL "Linux")
    return()
endif()

function(openusd_set_linux_runtime_paths directory)
    get_property(targets DIRECTORY "${directory}" PROPERTY BUILDSYSTEM_TARGETS)
    foreach(target IN LISTS targets)
        get_target_property(type "${target}" TYPE)
        if(type STREQUAL "SHARED_LIBRARY" OR type STREQUAL "MODULE_LIBRARY")
            set_target_properties("${target}" PROPERTIES
                INSTALL_RPATH "$ORIGIN"
                INSTALL_RPATH_USE_LINK_PATH FALSE
                BUILD_WITH_INSTALL_RPATH FALSE)
        elseif(type STREQUAL "EXECUTABLE")
            set_target_properties("${target}" PROPERTIES
                INSTALL_RPATH "$ORIGIN/../lib"
                INSTALL_RPATH_USE_LINK_PATH FALSE
                BUILD_WITH_INSTALL_RPATH FALSE)
        endif()
    endforeach()
    get_property(children DIRECTORY "${directory}" PROPERTY SUBDIRECTORIES)
    foreach(child IN LISTS children)
        openusd_set_linux_runtime_paths("${child}")
    endforeach()
endfunction()

# USD's _pxr_install_rpath and dependency projects override initial CMake defaults.
cmake_language(DEFER DIRECTORY "${CMAKE_SOURCE_DIR}"
    CALL openusd_set_linux_runtime_paths "${CMAKE_SOURCE_DIR}")
