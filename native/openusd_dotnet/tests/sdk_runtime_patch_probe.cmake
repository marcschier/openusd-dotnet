add_executable(openusd_sdk_runtime_patch_probe
    "${CMAKE_CURRENT_LIST_DIR}/sdk_runtime_patch_probe.cpp")
target_compile_features(openusd_sdk_runtime_patch_probe PRIVATE cxx_std_17)
target_link_libraries(openusd_sdk_runtime_patch_probe PRIVATE usd_m)
if(MSVC)
    target_compile_options(openusd_sdk_runtime_patch_probe PRIVATE /W4 /WX /permissive-)
else()
    target_compile_options(openusd_sdk_runtime_patch_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()
add_test(NAME openusd_sdk_runtime_patch_probe COMMAND openusd_sdk_runtime_patch_probe
    "${CMAKE_CURRENT_LIST_DIR}/fixtures/sdk-runtime")
set_tests_properties(openusd_sdk_runtime_patch_probe PROPERTIES TIMEOUT 30)
if(WIN32)
    set_tests_properties(openusd_sdk_runtime_patch_probe PROPERTIES ENVIRONMENT_MODIFICATION
        "PATH=path_list_prepend:${_openusd_test_prefix}/lib;PATH=path_list_prepend:${_openusd_test_prefix}/bin")
elseif(APPLE)
    set_tests_properties(openusd_sdk_runtime_patch_probe PROPERTIES ENVIRONMENT_MODIFICATION
        "DYLD_LIBRARY_PATH=path_list_prepend:${_openusd_test_prefix}/lib")
else()
    set_tests_properties(openusd_sdk_runtime_patch_probe PROPERTIES ENVIRONMENT_MODIFICATION
        "LD_LIBRARY_PATH=path_list_prepend:${_openusd_test_prefix}/lib")
endif()
