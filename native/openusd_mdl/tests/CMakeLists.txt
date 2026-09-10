# The probe drives whichever adapter it is pointed at through the public C ABI,
# so one executable covers both the dependency-free adapter and the SDK-backed
# one. Two tests, not two probes.
add_executable(mdl_sdk_probe mdl_sdk_probe.cpp)
target_compile_features(mdl_sdk_probe PRIVATE cxx_std_17)
target_include_directories(
    mdl_sdk_probe PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/../../include)
if(MSVC)
    target_compile_options(mdl_sdk_probe PRIVATE /W4 /WX /permissive-)
else()
    target_compile_options(mdl_sdk_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
    target_link_libraries(mdl_sdk_probe PRIVATE ${CMAKE_DL_LIBS})
endif()
add_dependencies(mdl_sdk_probe openusd_mdl)

get_filename_component(
    OPENUSD_MDL_FIXTURE_DIR
    "${CMAKE_CURRENT_SOURCE_DIR}/../../../test-assets/mdl"
    ABSOLUTE)

# The dependency-free adapter is passed no search path at all: it has no module
# to search for, and the probe's own checks assert exactly that shape.
add_test(
    NAME mdl_adapter_probe
    COMMAND mdl_sdk_probe $<TARGET_FILE:openusd_mdl>)

if(TARGET openusd_mdl_sdk)
    # No adapter import library: this same probe can load a preserved old DLL
    # before the SDK-backed adapter target is rebuilt.
    add_executable(mdl_alias_color_probe mdl_alias_color_probe.cpp)
    target_compile_features(mdl_alias_color_probe PRIVATE cxx_std_17)
    target_include_directories(mdl_alias_color_probe PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/../../include)
    if(MSVC)
        target_compile_options(mdl_alias_color_probe PRIVATE /W4 /WX /permissive- /EHsc)
    else()
        target_compile_options(mdl_alias_color_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
        target_link_libraries(mdl_alias_color_probe PRIVATE ${CMAKE_DL_LIBS})
    endif()
    add_test(NAME mdl_sdk_alias_color_probe
        COMMAND mdl_alias_color_probe $<TARGET_FILE:openusd_mdl_sdk>
            "${CMAKE_CURRENT_SOURCE_DIR}/fixtures")
    set_tests_properties(mdl_sdk_alias_color_probe PROPERTIES
        TIMEOUT 120
        ENVIRONMENT_MODIFICATION "OPENUSD_MDL_SDK_RUNTIME=set:${OPENUSD_MDL_SDK_RUNTIME_DIR}")
    add_executable(mdl_wrapper_probe mdl_wrapper_probe.cpp)
    target_compile_features(mdl_wrapper_probe PRIVATE cxx_std_17)
    target_link_libraries(mdl_wrapper_probe PRIVATE openusd_mdl_sdk)
    if(MSVC)
        target_compile_options(mdl_wrapper_probe PRIVATE /W4 /WX /permissive- /EHsc)
    else()
        target_compile_options(mdl_wrapper_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
    endif()
    add_test(NAME mdl_sdk_wrapper_rejections
        COMMAND mdl_wrapper_probe --rejections "${CMAKE_CURRENT_SOURCE_DIR}/fixtures")
    set(_mdl_wrapper_tests mdl_sdk_wrapper_rejections)
    if(OPENUSD_MDL_REAL_MODULE_ROOT)
        # Only repository-authored variants/images live here. Their real base
        # modules remain operator-supplied, opt-in, and outside every package.
        add_test(NAME mdl_sdk_wrapper_variants
            COMMAND mdl_wrapper_probe --synthetic
                "${CMAKE_CURRENT_SOURCE_DIR}/fixtures"
                "${OPENUSD_MDL_REAL_MODULE_ROOT}"
                "${CMAKE_CURRENT_BINARY_DIR}/wrapper-probe-work")
        list(APPEND _mdl_wrapper_tests mdl_sdk_wrapper_variants)
        set(OPENUSD_MDL_WAREHOUSE_SOURCE_ROOT "" CACHE PATH
            "Opt-in original SimReady warehouse source root for read-only wrapper qualification")
        if(OPENUSD_MDL_WAREHOUSE_SOURCE_ROOT)
            add_test(NAME mdl_warehouse_wrapper_projection
                COMMAND mdl_wrapper_probe --warehouse
                    "${OPENUSD_MDL_REAL_MODULE_ROOT}"
                    "${OPENUSD_MDL_WAREHOUSE_SOURCE_ROOT}")
            list(APPEND _mdl_wrapper_tests mdl_warehouse_wrapper_projection)
        endif()
    endif()
    set_tests_properties(${_mdl_wrapper_tests} PROPERTIES
        TIMEOUT 120
        ENVIRONMENT_MODIFICATION
            "PATH=path_list_prepend:$<TARGET_FILE_DIR:openusd_mdl_sdk>;OPENUSD_MDL_SDK_RUNTIME=set:${OPENUSD_MDL_SDK_RUNTIME_DIR}")
    add_executable(mdl_module_probe mdl_module_probe.cpp)
    target_compile_features(mdl_module_probe PRIVATE cxx_std_17)
    target_link_libraries(mdl_module_probe PRIVATE openusd_mdl_sdk)
    if(MSVC)
        target_compile_options(mdl_module_probe PRIVATE /W4 /WX /permissive- /EHsc)
    else()
        target_compile_options(mdl_module_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
    endif()
    if(OPENUSD_MDL_REAL_MODULE_ROOT)
        add_test(NAME mdl_real_module_defaults
            COMMAND mdl_module_probe "${OPENUSD_MDL_REAL_MODULE_ROOT}")
        set_tests_properties(mdl_real_module_defaults PROPERTIES
            TIMEOUT 120
            ENVIRONMENT_MODIFICATION
                "PATH=path_list_prepend:$<TARGET_FILE_DIR:openusd_mdl_sdk>;OPENUSD_MDL_SDK_RUNTIME=set:${OPENUSD_MDL_SDK_RUNTIME_DIR}")
    endif()
    add_dependencies(mdl_sdk_probe openusd_mdl_sdk)
    # OPENUSD_MDL_SDK_RUNTIME points the backend at the user-supplied libmdl_sdk.
    # It is set here from the acquisition tree rather than baked into the adapter,
    # because the runtime is never redistributed and an operator supplies their
    # own. Without it the probe reports the unavailable state and still passes,
    # which is what keeps this test runnable on a machine with no SDK.
    add_test(
        NAME mdl_sdk_adapter_probe
        COMMAND mdl_sdk_probe $<TARGET_FILE:openusd_mdl_sdk> ${OPENUSD_MDL_FIXTURE_DIR})
    if(OPENUSD_MDL_SDK_RUNTIME_DIR)
        set_tests_properties(
            mdl_sdk_adapter_probe
            PROPERTIES
                ENVIRONMENT_MODIFICATION
                    "OPENUSD_MDL_SDK_RUNTIME=set:${OPENUSD_MDL_SDK_RUNTIME_DIR}")
    endif()
endif()
