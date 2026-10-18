# Copyright (c) marcschier. Licensed under the MIT License.

set(CMAKE_COMPILE_WARNING_AS_ERROR ON)

# Shaderc's third-party directory adds -w before its dependencies add their
# own warning levels. Retain those levels rather than blanket suppression.
function(openusd_shader_compiler_warnings directory)
    get_property(targets DIRECTORY "${directory}" PROPERTY BUILDSYSTEM_TARGETS)
    foreach(target IN LISTS targets)
        get_target_property(options "${target}" COMPILE_OPTIONS)
        if(options)
            list(REMOVE_ITEM options "-w" "/w")
            set_property(TARGET "${target}" PROPERTY COMPILE_OPTIONS "${options}")
        endif()
    endforeach()
    get_property(children DIRECTORY "${directory}" PROPERTY SUBDIRECTORIES)
    foreach(child IN LISTS children)
        openusd_shader_compiler_warnings("${child}")
    endforeach()
endfunction()

cmake_language(DEFER CALL openusd_shader_compiler_warnings "${CMAKE_SOURCE_DIR}")
