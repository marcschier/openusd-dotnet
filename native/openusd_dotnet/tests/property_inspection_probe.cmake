# Copyright (c) marcschier. Licensed under the MIT License.

add_executable(openusd_property_inspection_probe "${CMAKE_CURRENT_LIST_DIR}/property_inspection_probe.cpp")
add_executable(openusd_property_admission_probe "${CMAKE_CURRENT_LIST_DIR}/property_admission_probe.cpp")
add_executable(openusd_property_composition_probe "${CMAKE_CURRENT_LIST_DIR}/property_composition_probe.cpp")
foreach(probe openusd_property_inspection_probe openusd_property_admission_probe openusd_property_composition_probe)
    target_compile_features(${probe} PRIVATE cxx_std_17)
    target_link_libraries(${probe} PRIVATE openusd_dotnet usd_m)
    if(TARGET openusd_native_sanitizers)
        target_link_libraries(${probe} PRIVATE openusd_native_sanitizers)
    endif()
    if(MSVC)
        target_compile_options(${probe} PRIVATE /W4 /WX /permissive-)
    else()
        target_compile_options(${probe} PRIVATE -Wall -Wextra -Wpedantic -Werror)
    endif()
endforeach()
if(WIN32)
    target_link_libraries(openusd_property_admission_probe PRIVATE psapi)
endif()
file(MAKE_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/property-probe")
add_test(NAME openusd_property_inspection_probe COMMAND openusd_property_inspection_probe
    "${_openusd_test_prefix}/lib/usd" "${CMAKE_CURRENT_BINARY_DIR}/property-probe")
set(_property_tests openusd_property_inspection_probe)
foreach(mode string mismatched-default array properties property-order times array-edit sample-array-edit cstring source-path variant-set variant-selection targets prim-errors layer-errors layer-error-list)
    add_test(NAME openusd_property_admission_${mode} COMMAND openusd_property_admission_probe
        "${_openusd_test_prefix}/lib/usd" "${CMAKE_CURRENT_BINARY_DIR}/property-probe/${mode}.usda" "${mode}")
    list(APPEND _property_tests openusd_property_admission_${mode})
endforeach()
foreach(mode external internal external-local internal-local layer-stack ancestor unrelated repair)
    add_test(NAME openusd_property_composition_${mode} COMMAND openusd_property_composition_probe
        "${_openusd_test_prefix}/lib/usd" "${CMAKE_CURRENT_BINARY_DIR}/property-probe" "${mode}")
    list(APPEND _property_tests openusd_property_composition_${mode})
endforeach()
set_tests_properties(${_property_tests} PROPERTIES TIMEOUT 120)
add_test(NAME openusd_property_pcp_header_identity
    COMMAND "${CMAKE_COMMAND}"
        "-DREPO_ROOT=${CMAKE_CURRENT_LIST_DIR}/../../.."
        "-DSDK_INCLUDE_ROOT=${_openusd_test_prefix}/include"
        "-DWORK_ROOT=${CMAKE_CURRENT_BINARY_DIR}/property-header-contract"
        "-DBUILD_GENERATOR=${CMAKE_GENERATOR}"
        "-DBUILD_MAKE_PROGRAM=${CMAKE_MAKE_PROGRAM}"
        -P "${CMAKE_CURRENT_LIST_DIR}/property_pcp_headers_probe.cmake")
if(WIN32)
    set_tests_properties(${_property_tests} PROPERTIES ENVIRONMENT_MODIFICATION
        "PATH=path_list_prepend:${_openusd_test_prefix}/lib;PATH=path_list_prepend:${_openusd_test_prefix}/bin;PATH=path_list_prepend:$<TARGET_FILE_DIR:openusd_dotnet>")
elseif(APPLE)
    set_tests_properties(${_property_tests} PROPERTIES ENVIRONMENT_MODIFICATION
        "DYLD_LIBRARY_PATH=path_list_prepend:${_openusd_test_prefix}/lib")
else()
    set_tests_properties(${_property_tests} PROPERTIES ENVIRONMENT_MODIFICATION
        "LD_LIBRARY_PATH=path_list_prepend:${_openusd_test_prefix}/lib")
endif()
