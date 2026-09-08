# Copyright (c) marcschier. Licensed under the MIT License.

add_executable(openusd_hierarchy_probe "${CMAKE_CURRENT_LIST_DIR}/hierarchy_probe.cpp")
target_compile_features(openusd_hierarchy_probe PRIVATE cxx_std_17)
target_link_libraries(openusd_hierarchy_probe PRIVATE openusd_dotnet usd_m)
if(TARGET openusd_native_sanitizers)
    target_link_libraries(openusd_hierarchy_probe PRIVATE openusd_native_sanitizers)
endif()
if(MSVC)
    target_compile_options(openusd_hierarchy_probe PRIVATE /W4 /WX /permissive-)
else()
    target_compile_options(openusd_hierarchy_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()
file(MAKE_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/hierarchy-probe")
add_test(NAME openusd_hierarchy_probe COMMAND openusd_hierarchy_probe
    "${_openusd_test_prefix}/lib/usd" "${CMAKE_CURRENT_BINARY_DIR}/hierarchy-probe")
if(WIN32)
    set_tests_properties(openusd_hierarchy_probe PROPERTIES ENVIRONMENT_MODIFICATION
        "PATH=path_list_prepend:${_openusd_test_prefix}/lib;PATH=path_list_prepend:${_openusd_test_prefix}/bin;PATH=path_list_prepend:$<TARGET_FILE_DIR:openusd_dotnet>")
elseif(APPLE)
    set_tests_properties(openusd_hierarchy_probe PROPERTIES ENVIRONMENT_MODIFICATION
        "DYLD_LIBRARY_PATH=path_list_prepend:${_openusd_test_prefix}/lib")
else()
    set_tests_properties(openusd_hierarchy_probe PROPERTIES ENVIRONMENT_MODIFICATION
        "LD_LIBRARY_PATH=path_list_prepend:${_openusd_test_prefix}/lib")
endif()
