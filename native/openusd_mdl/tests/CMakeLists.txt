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
