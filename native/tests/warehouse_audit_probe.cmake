add_executable(openusd_warehouse_audit_probe "${CMAKE_CURRENT_LIST_DIR}/warehouse_audit_probe.cpp")
target_compile_features(openusd_warehouse_audit_probe PRIVATE cxx_std_17)
target_link_libraries(openusd_warehouse_audit_probe PRIVATE usd_m)
if(MSVC)
    target_compile_options(openusd_warehouse_audit_probe PRIVATE /W4 /WX /permissive- /EHsc)
else()
    target_compile_options(openusd_warehouse_audit_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()
add_test(NAME openusd_warehouse_audit_contract
    COMMAND openusd_warehouse_audit_probe --self-test)
add_test(NAME openusd_warehouse_localization_contract
    COMMAND openusd_warehouse_audit_probe --localization-self-test)
set_tests_properties(openusd_warehouse_audit_contract openusd_warehouse_localization_contract
    PROPERTIES TIMEOUT 30)
if(WIN32)
    set_tests_properties(openusd_warehouse_audit_contract openusd_warehouse_localization_contract PROPERTIES
        ENVIRONMENT_MODIFICATION
            "PATH=path_list_prepend:${_openusd_test_prefix}/bin;PATH=path_list_prepend:${_openusd_test_prefix}/lib")
elseif(APPLE)
    set_tests_properties(openusd_warehouse_audit_contract openusd_warehouse_localization_contract PROPERTIES
        ENVIRONMENT_MODIFICATION
            "DYLD_LIBRARY_PATH=path_list_prepend:${_openusd_test_prefix}/lib")
else()
    set_tests_properties(openusd_warehouse_audit_contract openusd_warehouse_localization_contract PROPERTIES
        ENVIRONMENT_MODIFICATION
            "LD_LIBRARY_PATH=path_list_prepend:${_openusd_test_prefix}/lib")
endif()
