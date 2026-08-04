add_executable(openusd_physx_probe main.cpp)
target_compile_features(openusd_physx_probe PRIVATE cxx_std_17)
target_link_libraries(openusd_physx_probe PRIVATE openusd_physx)
if(MSVC)
    target_compile_options(openusd_physx_probe PRIVATE /W4 /WX /permissive-)
else()
    target_compile_options(openusd_physx_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

add_test(NAME openusd_physx_abi_probe COMMAND openusd_physx_probe)
if(WIN32)
    set(openusd_physx_probe_path
        "PATH=path_list_prepend:$<TARGET_FILE_DIR:openusd_physx>"
        "PATH=path_list_prepend:$ENV{OPENUSD_ROOT}/lib"
        "PATH=path_list_prepend:$ENV{OPENUSD_ROOT}/bin"
        "PATH=path_list_prepend:$ENV{VULKAN_SDK}/Bin")
    set_tests_properties(
        openusd_physx_abi_probe
        PROPERTIES
            ENVIRONMENT_MODIFICATION "${openusd_physx_probe_path}")
endif()