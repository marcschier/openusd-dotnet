# Copyright (c) marcschier. Licensed under the MIT License.

include_guard(GLOBAL)
include("${CMAKE_CURRENT_LIST_DIR}/VerifyPropertyPcpHeaders.cmake")

function(openusd_configure_property_pcp_compatibility)
    if(NOT TARGET pcp)
        message(FATAL_ERROR "The pinned PCP target is required for property inspection.")
    endif()
    get_target_property(includes pcp INTERFACE_INCLUDE_DIRECTORIES)
    set(roots)
    foreach(directory IN LISTS includes)
        if(EXISTS "${directory}/pxr/usd/pcp/primIndex.h")
            file(REAL_PATH "${directory}" directory)
            list(APPEND roots "${directory}")
        endif()
    endforeach()
    list(REMOVE_DUPLICATES roots)
    list(LENGTH roots count)
    if(NOT count EQUAL 1)
        message(FATAL_ERROR "Exactly one concrete pinned PCP include root is required.")
    endif()
    list(GET roots 0 root)
    file(TO_CMAKE_PATH "${root}" root)
    openusd_verify_property_pcp_headers("${root}")
    set(OPENUSD_PROPERTY_PCP_INCLUDE_ROOT "${root}" PARENT_SCOPE)
    add_custom_target(openusd_verify_property_pcp_headers
        COMMAND "${CMAKE_COMMAND}" "-DPCP_INCLUDE_ROOT=${root}"
            -P "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/VerifyPropertyPcpHeaders.cmake"
        COMMENT "Verifying pinned PCP property-inspection header identities"
        VERBATIM)
endfunction()
