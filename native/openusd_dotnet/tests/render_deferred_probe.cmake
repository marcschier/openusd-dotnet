# Copyright (c) marcschier. Licensed under the MIT License.
function(openusd_add_render_deferred_probes sdk_root)
    add_executable(openusd_render_deferred_probe
        "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/render_deferred_probe.cpp")
    target_link_libraries(openusd_render_deferred_probe PRIVATE openusd_dotnet usd_m)
    set(probes openusd_render_deferred_probe)
    if(EXISTS "${sdk_root}/include/pxr/usd/sdf/storageAdmission.h")
        add_executable(openusd_render_storage_admission_probe
            "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/render_storage_admission_probe.cpp")
        target_link_libraries(openusd_render_storage_admission_probe PRIVATE usd_m)
        list(APPEND probes openusd_render_storage_admission_probe)
        add_executable(openusd_render_crate_admission_probe
            "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/render_crate_admission_probe.cpp")
        target_link_libraries(openusd_render_crate_admission_probe PRIVATE openusd_dotnet usd_m)
        list(APPEND probes openusd_render_crate_admission_probe)
        set(crate_modes original-small original-large dirty-small dirty-large
            nested-zero nested-generous nested-sticky nested-absence nested-accounting nested-threads)
        if(WIN32)
            target_link_libraries(openusd_render_crate_admission_probe PRIVATE psapi)
            list(APPEND crate_modes large-array large-encoded-edit large-intermediate)
        endif()
        foreach(mode IN LISTS crate_modes)
            add_test(NAME openusd_render_crate_${mode}
                COMMAND openusd_render_crate_admission_probe
                    "${sdk_root}/lib/usd" "${CMAKE_CURRENT_BINARY_DIR}" "${mode}")
            set_tests_properties(openusd_render_crate_${mode} PROPERTIES TIMEOUT 120
                ENVIRONMENT_MODIFICATION
                "PATH=path_list_prepend:${sdk_root}/lib;PATH=path_list_prepend:${sdk_root}/bin;PATH=path_list_prepend:$<TARGET_FILE_DIR:openusd_dotnet>")
        endforeach()
        add_test(NAME openusd_render_storage_admission_probe
            COMMAND openusd_render_storage_admission_probe "${CMAKE_CURRENT_BINARY_DIR}")
        set_tests_properties(openusd_render_storage_admission_probe PROPERTIES ENVIRONMENT_MODIFICATION
            "PATH=path_list_prepend:${sdk_root}/lib;PATH=path_list_prepend:${sdk_root}/bin")
    endif()
    foreach(probe IN LISTS probes)
        target_compile_features(${probe} PRIVATE cxx_std_17)
        if(MSVC)
            target_compile_options(${probe} PRIVATE /W4 /WX /permissive-)
        else()
            target_compile_options(${probe} PRIVATE -Wall -Wextra -Wpedantic -Werror)
        endif()
    endforeach()
    set(modes usda array-edit edit-empty edit-identity edit-modify edit-oversized edit-peak
        edit-out-of-range edit-layered)
    if(EXISTS "${sdk_root}/include/pxr/usd/sdf/storageAdmission.h")
        list(APPEND modes usdc usdc-array-edit usdc-edit-empty usdc-edit-identity
            usdc-edit-modify usdc-edit-oversized usdc-edit-peak usdc-edit-out-of-range
            usdc-edit-layered usdc-edit-compressed usdc-forwarded usdc-absent-with-errors usdc-ancestor-errors)
    endif()
    foreach(mode IN LISTS modes)
        add_test(NAME openusd_render_deferred_${mode} COMMAND openusd_render_deferred_probe
            "${sdk_root}/lib/usd" "${CMAKE_CURRENT_BINARY_DIR}" "${mode}")
        set_tests_properties(openusd_render_deferred_${mode} PROPERTIES ENVIRONMENT_MODIFICATION
            "PATH=path_list_prepend:${sdk_root}/lib;PATH=path_list_prepend:${sdk_root}/bin;PATH=path_list_prepend:$<TARGET_FILE_DIR:openusd_dotnet>")
    endforeach()
endfunction()
