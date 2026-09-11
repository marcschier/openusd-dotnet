# Copyright (c) marcschier. Licensed under the MIT License.

include_guard(GLOBAL)

function(openusd_configure_native_fuzz_targets shim_target harness_source)
    if(NOT OPENUSD_BUILD_NATIVE_FUZZERS)
        return()
    endif()
    if(NOT CMAKE_SYSTEM_NAME STREQUAL "Linux" OR
       NOT CMAKE_CXX_COMPILER_ID STREQUAL "Clang")
        message(
            FATAL_ERROR
            "OPENUSD_BUILD_NATIVE_FUZZERS requires Linux with Clang and libFuzzer.")
    endif()

    target_compile_options(
        ${shim_target}
        PRIVATE
            -fsanitize=fuzzer-no-link,address,undefined
            -fno-omit-frame-pointer
            -fno-sanitize-recover=all)
    target_link_options(
        ${shim_target}
        PRIVATE
            -fsanitize=address,undefined
            -fno-sanitize-recover=all)

    add_executable(openusd_stage_layer_fuzzer "${harness_source}")
    target_compile_features(openusd_stage_layer_fuzzer PRIVATE cxx_std_17)
    target_link_libraries(openusd_stage_layer_fuzzer PRIVATE ${shim_target})
    target_compile_options(
        openusd_stage_layer_fuzzer
        PRIVATE
            -Wall
            -Wextra
            -Wpedantic
            -Werror
            -fsanitize=fuzzer,address,undefined
            -fno-omit-frame-pointer
            -fno-sanitize-recover=all)
    target_link_options(
        openusd_stage_layer_fuzzer
        PRIVATE
            -fsanitize=fuzzer,address,undefined
            -fno-sanitize-recover=all)
    set_target_properties(
        openusd_stage_layer_fuzzer
        PROPERTIES RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/fuzz")
endfunction()
