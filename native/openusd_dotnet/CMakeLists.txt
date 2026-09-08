set(OPENUSD_DOTNET_SOURCES
    include/openusd_dotnet.h
    include/openusd_physics_extract.h
    private/openusd_renderer_stage_bridge.h
    include/openusd_layer_edit.h
    include/openusd_review_document.h
    include/openusd_hierarchy.h
    include/openusd_property_inspection.h
    include/openusd_image_exr.h
    src/attribute.cpp
    src/collections.cpp
    src/common.cpp
    src/composition.cpp
    src/debug.cpp
    src/diagnostics.cpp
    src/hierarchy.cpp
    src/hierarchy_variants.cpp
    src/image_exr.cpp
    src/layer.cpp
    src/layer_edit.cpp
    src/layer_edit_codec.cpp
    src/layer_edit_values.cpp
    src/layer_edit_transaction.cpp
    src/layer_edit_checkpoint.cpp
    src/review_document.cpp
    src/review_codec.cpp
    src/review_source.cpp
    src/review_data.cpp
    src/review_inspection_schema.cpp
    src/review_values.cpp
    src/review_composition.cpp
    src/review_files.cpp
    src/metadata.cpp
    src/ocio.cpp
    src/pcp.cpp
    src/property_composition.cpp
    src/property_inspection.cpp
    src/property_sources.cpp
    src/property_values.cpp
    src/physics_bake.cpp
    src/physics_extract.cpp
    src/relationship.cpp
    src/renderer_stage_bridge.cpp
    src/resolver.cpp
    src/runtime.cpp
    src/sdr.cpp
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
    src/render_specification.cpp
    src/render_array_edit.cpp
    src/usdShade.cpp
    src/usdSkel.cpp
    src/usdUI.cpp
    src/usdVol.cpp)

include("${CMAKE_CURRENT_SOURCE_DIR}/cmake/PropertyPcpCompatibility.cmake")
openusd_configure_property_pcp_compatibility()
configure_file(cmake/property_pcp_headers.g.h.in generated/property_pcp_headers.g.h @ONLY)
include("${CMAKE_CURRENT_SOURCE_DIR}/cmake/VerifyRenderArrayEditHeaders.cmake")
configure_file(cmake/render_array_edit_headers.g.h.in generated/render_array_edit_headers.g.h @ONLY)
set(OPENUSD_WITH_STORAGE_ADMISSION OFF)
if(EXISTS "${OPENUSD_PROPERTY_PCP_INCLUDE_ROOT}/pxr/usd/sdf/storageAdmission.h")
    file(STRINGS "${OPENUSD_PROPERTY_PCP_INCLUDE_ROOT}/pxr/usd/sdf/storageAdmission.h" storage_version
        REGEX "^#define SDF_STORAGE_ADMISSION_VERSION ")
    if(NOT storage_version STREQUAL "#define SDF_STORAGE_ADMISSION_VERSION 1u")
        message(FATAL_ERROR "Unsupported SDK storage-admission accessor version.")
    endif()
    get_filename_component(storage_sdk_root "${OPENUSD_PROPERTY_PCP_INCLUDE_ROOT}/.." ABSOLUTE)
    get_filename_component(storage_patch_lock
        "${CMAKE_CURRENT_SOURCE_DIR}/../../eng/openusd-storage-admission.lock.json" ABSOLUTE)
    include("${CMAKE_CURRENT_SOURCE_DIR}/cmake/VerifyStorageAdmissionSdk.cmake")
    openusd_verify_storage_admission_sdk("${storage_sdk_root}" "${storage_patch_lock}")
    configure_file(cmake/storage_admission_header.g.h.in generated/storage_admission_header.g.h @ONLY)
    add_custom_target(openusd_verify_storage_admission_sdk
        COMMAND "${CMAKE_COMMAND}" "-DSTORAGE_ADMISSION_SDK_ROOT=${storage_sdk_root}"
            "-DSTORAGE_ADMISSION_PATCH_LOCK=${storage_patch_lock}"
            -P "${CMAKE_CURRENT_SOURCE_DIR}/cmake/VerifyStorageAdmissionSdk.cmake"
        COMMENT "Verifying exact storage-admission SDK source/header/binary provenance"
        VERBATIM)
    set(OPENUSD_WITH_STORAGE_ADMISSION ON)
else()
    openusd_verify_render_array_edit_headers("${OPENUSD_PROPERTY_PCP_INCLUDE_ROOT}")
    add_custom_target(openusd_verify_render_array_edit_headers
        COMMAND "${CMAKE_COMMAND}" "-DRENDER_EDIT_INCLUDE_ROOT=${OPENUSD_PROPERTY_PCP_INCLUDE_ROOT}"
            -P "${CMAKE_CURRENT_SOURCE_DIR}/cmake/VerifyRenderArrayEditHeaders.cmake"
        COMMENT "Verifying pinned render array-edit headers"
        VERBATIM)
endif()

add_library(openusd_dotnet SHARED ${OPENUSD_DOTNET_SOURCES})

include(CheckCXXSourceCompiles)
check_cxx_source_compiles("
#if !defined(_WIN32) || (!defined(_M_X64) && !defined(__x86_64__))
#error Guarded EXR file-handle profile unavailable
#endif
int main() { return 0; }
" OPENUSD_IMAGE_EXR_WINDOWS_X64)
if(OPENUSD_IMAGE_EXR_WINDOWS_X64)
    find_package(OpenEXR 3.1 CONFIG REQUIRED)
endif()

function(configure_openusd_dotnet_target target_name)
    add_dependencies(${target_name} openusd_verify_property_pcp_headers)
    if(OPENUSD_WITH_STORAGE_ADMISSION)
        add_dependencies(${target_name} openusd_verify_storage_admission_sdk)
        target_compile_definitions(${target_name} PRIVATE OPENUSD_WITH_STORAGE_ADMISSION)
    else()
        add_dependencies(${target_name} openusd_verify_render_array_edit_headers)
    endif()
    target_compile_features(${target_name} PRIVATE cxx_std_17)
    target_compile_definitions(${target_name} PRIVATE OPENUSD_DOTNET_BUILD)
    target_include_directories(${target_name}
        PUBLIC
            $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
            $<INSTALL_INTERFACE:include>)
    target_include_directories(
        ${target_name}
        PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/private "${CMAKE_CURRENT_BINARY_DIR}/generated")
    target_link_libraries(${target_name} PRIVATE usd_m)
    target_link_libraries(${target_name} PRIVATE OpenColorIO::OpenColorIO)
    if(OPENUSD_IMAGE_EXR_WINDOWS_X64)
        target_link_libraries(${target_name} PRIVATE OpenEXR::OpenEXR ntdll)
    endif()
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
if(WIN32 AND MSVC)
    # Keep published data export ordinals stable in every ordinary Windows build.
    target_sources(openusd_dotnet PRIVATE private/openusd_dotnet.def)
endif()

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
install(FILES include/openusd_dotnet.h include/openusd_physics_extract.h include/openusd_layer_edit.h
    include/openusd_review_document.h
    include/openusd_hierarchy.h
    include/openusd_property_inspection.h
    include/openusd_image_exr.h
    DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
