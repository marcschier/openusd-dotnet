# Copyright (c) marcschier. Licensed under the MIT License.

function(openusd_verify_render_array_edit_headers include_root)
    set(headers
        arrayEdit.h DEFB0C313A756B7602813D95B9046CF32C606E02640A12EC7C42CA8EBFE6B36A
        arrayEditOps.h D6D7DE38821CA8096F105D20D66F0BDF4ECC8A2FCE9ACE9D41F6F097D8CF4C3F)
    foreach(index 0 2)
        math(EXPR hash_index "${index} + 1")
        list(GET headers ${index} name)
        list(GET headers ${hash_index} expected)
        set(path "${include_root}/pxr/base/vt/${name}")
        if(NOT EXISTS "${path}")
            message(FATAL_ERROR "Missing pinned render array-edit header: ${name}")
        endif()
        file(SHA256 "${path}" actual)
        string(TOUPPER "${actual}" actual)
        if(NOT actual STREQUAL expected)
            message(FATAL_ERROR "Render array-edit header identity mismatch: ${name}")
        endif()
    endforeach()
endfunction()

if(CMAKE_SCRIPT_MODE_FILE STREQUAL CMAKE_CURRENT_LIST_FILE)
    if(NOT DEFINED RENDER_EDIT_INCLUDE_ROOT)
        message(FATAL_ERROR "RENDER_EDIT_INCLUDE_ROOT is required.")
    endif()
    openusd_verify_render_array_edit_headers("${RENDER_EDIT_INCLUDE_ROOT}")
endif()
