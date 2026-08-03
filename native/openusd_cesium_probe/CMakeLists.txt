add_executable(openusd_cesium_probe main.cpp)
target_compile_features(openusd_cesium_probe PRIVATE cxx_std_17)
target_link_libraries(openusd_cesium_probe PRIVATE openusd_cesium)
if(MSVC)
    target_compile_options(openusd_cesium_probe PRIVATE /W4 /WX /permissive-)
else()
    target_compile_options(openusd_cesium_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

add_test(NAME openusd_cesium_abi_probe COMMAND openusd_cesium_probe)
add_test(NAME openusd_cesium_abi_probe_asset_failure COMMAND openusd_cesium_probe --expect-asset-failure)
add_test(NAME openusd_cesium_abi_probe_renderer_break COMMAND openusd_cesium_probe --expect-renderer-break)
if(WIN32)
    set_tests_properties(
        openusd_cesium_abi_probe
        openusd_cesium_abi_probe_asset_failure
        openusd_cesium_abi_probe_renderer_break
        PROPERTIES
            ENVIRONMENT_MODIFICATION
                "PATH=path_list_prepend:$<TARGET_FILE_DIR:openusd_cesium>")
endif()