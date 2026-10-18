# Copyright (c) marcschier. Licensed under the MIT License.

function(openusd_verify_property_pcp_headers include_root)
    # OpenUSD26.08, commitee47c679abde5b467a7b6a41f3b2285564a4222e.
    if(NOT EXISTS "${include_root}/pxr/pxr.h")
        message(FATAL_ERROR "Missing pinned PCP SDK version header.")
    endif()
    file(STRINGS "${include_root}/pxr/pxr.h" version
        REGEX "^#define[ \t]+PXR_VERSION[ \t]+[0-9]+")
    if(NOT version MATCHES "^#define[ \t]+PXR_VERSION[ \t]+2608[ \t]*$")
        message(FATAL_ERROR "PCP SDK version mismatch: the accessor requires the reviewed version 2608.")
    endif()
    set(headers
        primIndex.h B6D8037DBD13A1C9D8A4CF30AA3ACCB9B28C302B2DC788AB9268D50753015990
        layerStack.h 6EC967C527894DBFC983172806F77EBC6EF47CF69006750213217AA476C7D5F5
        errors.h 368265EE0D486FFD412CA62EFD06E9748706C60F09C09AFD520518F226E51A0C)
    foreach(index 0 2 4)
        math(EXPR hash_index "${index} + 1")
        list(GET headers ${index} name)
        list(GET headers ${hash_index} expected)
        set(path "${include_root}/pxr/usd/pcp/${name}")
        if(NOT EXISTS "${path}")
            message(FATAL_ERROR "Missing pinned PCP header: ${name}")
        endif()
        file(SHA256 "${path}" actual)
        string(TOUPPER "${actual}" actual)
        if(NOT actual STREQUAL expected)
            message(FATAL_ERROR
                "PCP header identity mismatch: ${name}. The private property-inspection accessor "
                "requires the reviewed OpenUSD26.08 header; expected ${expected}, observed ${actual}.")
        endif()
    endforeach()
endfunction()

if(CMAKE_SCRIPT_MODE_FILE STREQUAL CMAKE_CURRENT_LIST_FILE)
    if(NOT DEFINED PCP_INCLUDE_ROOT OR PCP_INCLUDE_ROOT STREQUAL "")
        message(FATAL_ERROR "PCP_INCLUDE_ROOT is required for pinned header verification.")
    endif()
    openusd_verify_property_pcp_headers("${PCP_INCLUDE_ROOT}")
endif()
