set(OPENUSD_DOTNET_SOURCES
    include/openusd_dotnet.h
    include/openusd_physics_extract.h
    private/openusd_renderer_stage_bridge.h
    src/attribute.cpp
    src/collections.cpp
    src/common.cpp
    src/composition.cpp
    src/debug.cpp
    src/diagnostics.cpp
    src/layer.cpp
    src/metadata.cpp
    src/pcp.cpp
    src/physics_bake.cpp
    src/physics_extract.cpp
    src/relationship.cpp
    src/renderer_stage_bridge.cpp
    src/runtime.cpp
    src/session_overlay.cpp
    src/stage.cpp
    src/ts.cpp
    src/validation.cpp
    src/usdGeom.cpp
    src/usdLux.cpp
    src/usdPhysics.cpp
    src/usdMedia.cpp
    src/usdProc.cpp
    src/usdRender.cpp
    src/usdShade.cpp
    src/usdSkel.cpp
    src/usdUI.cpp
    src/usdVol.cpp)

add_library(openusd_dotnet SHARED ${OPENUSD_DOTNET_SOURCES})

function(configure_openusd_dotnet_target target_name)
    target_compile_features(${target_name} PRIVATE cxx_std_17)
    target_compile_definitions(${target_name} PRIVATE OPENUSD_DOTNET_BUILD)
    target_include_directories(${target_name}
        PUBLIC
            $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
            $<INSTALL_INTERFACE:include>)
    target_include_directories(
        ${target_name}
        PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/private)
    target_link_libraries(${target_name} PRIVATE usd_m)
    if(TARGET openusd_native_sanitizers)
        target_link_libraries(${target_name} PRIVATE openusd_native_sanitizers)
    endif()

    if(MSVC)
        target_compile_options(${target_name} PRIVATE /W4 /WX /permissive-)
    else()
        target_compile_options(${target_name} PRIVATE -Wall -Wextra -Wpedantic -Werror)
    endif()
endfunction()

configure_openusd_dotnet_target(openusd_dotnet)

if(OPENUSD_BUILD_NATIVE_FUZZERS)
    if(NOT CMAKE_SYSTEM_NAME STREQUAL "Linux" OR
       NOT CMAKE_CXX_COMPILER_ID STREQUAL "Clang")
        message(
            FATAL_ERROR
            "OPENUSD_BUILD_NATIVE_FUZZERS requires Linux with Clang and libFuzzer.")
    endif()

    target_compile_options(
        openusd_dotnet
        PRIVATE
            -fsanitize=fuzzer-no-link,address,undefined
            -fno-omit-frame-pointer
            -fno-sanitize-recover=all)
    target_link_options(
        openusd_dotnet
        PRIVATE
            -fsanitize=address,undefined
            -fno-sanitize-recover=all)

    add_executable(
        openusd_stage_layer_fuzzer
        tests/fuzz/stage_layer_fuzzer.cpp)
    target_compile_features(openusd_stage_layer_fuzzer PRIVATE cxx_std_17)
    target_link_libraries(openusd_stage_layer_fuzzer PRIVATE openusd_dotnet)
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
endif()

if(OPENUSD_BUILD_NATIVE_TESTS)
    add_library(openusd_dotnet_test STATIC
        tests/openusd_dotnet_test_hooks.h
        ${OPENUSD_DOTNET_SOURCES})
    configure_openusd_dotnet_target(openusd_dotnet_test)
    target_compile_definitions(
        openusd_dotnet_test
        PRIVATE OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
    target_include_directories(
        openusd_dotnet_test
        PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/tests)

    add_library(openusd_dotnet_renderer_test SHARED
        tests/openusd_dotnet_test_hooks.h
        ${OPENUSD_DOTNET_SOURCES})
    configure_openusd_dotnet_target(openusd_dotnet_renderer_test)
    target_compile_definitions(
        openusd_dotnet_renderer_test
        PRIVATE OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
    target_include_directories(
        openusd_dotnet_renderer_test
        PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/tests)
endif()

include(GNUInstallDirs)
install(TARGETS openusd_dotnet
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
    LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}
    ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR})
install(FILES include/openusd_dotnet.h include/openusd_physics_extract.h DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
