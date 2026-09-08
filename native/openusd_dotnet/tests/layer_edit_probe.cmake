add_executable(openusd_layer_edit_probe
    "${CMAKE_CURRENT_LIST_DIR}/layer_edit_probe.cpp"
    "${CMAKE_CURRENT_LIST_DIR}/layer_edit_cases.cpp"
    "${CMAKE_CURRENT_LIST_DIR}/layer_edit_cases.h"
    "${CMAKE_CURRENT_LIST_DIR}/layer_edit_probe_support.h")
target_compile_features(openusd_layer_edit_probe PRIVATE cxx_std_17)
target_compile_definitions(openusd_layer_edit_probe PRIVATE
    OPENUSD_DOTNET_BUILD OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
target_include_directories(openusd_layer_edit_probe PRIVATE "${CMAKE_CURRENT_LIST_DIR}/../private")
target_link_libraries(openusd_layer_edit_probe PRIVATE openusd_dotnet_test usd_m)
if(TARGET openusd_native_sanitizers)
    target_link_libraries(openusd_layer_edit_probe PRIVATE openusd_native_sanitizers)
endif()
if(MSVC)
    target_compile_options(openusd_layer_edit_probe PRIVATE /W4 /WX /permissive-)
else()
    target_compile_options(openusd_layer_edit_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()
file(MAKE_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/layer-edit-probe")
add_test(NAME openusd_layer_edit_probe
    COMMAND openusd_layer_edit_probe "${_openusd_test_prefix}/lib/usd"
        "${CMAKE_CURRENT_BINARY_DIR}/layer-edit-probe")
if(WIN32)
    set_tests_properties(openusd_layer_edit_probe PROPERTIES ENVIRONMENT_MODIFICATION
        "PATH=path_list_prepend:${_openusd_test_prefix}/lib;PATH=path_list_prepend:${_openusd_test_prefix}/bin")
elseif(APPLE)
    set_tests_properties(openusd_layer_edit_probe PROPERTIES ENVIRONMENT_MODIFICATION
        "DYLD_LIBRARY_PATH=path_list_prepend:${_openusd_test_prefix}/lib")
else()
    set_tests_properties(openusd_layer_edit_probe PROPERTIES ENVIRONMENT_MODIFICATION
        "LD_LIBRARY_PATH=path_list_prepend:${_openusd_test_prefix}/lib")
endif()
