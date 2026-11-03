cmake_minimum_required(VERSION 3.28)

if(CMAKE_SOURCE_DIR STREQUAL CMAKE_CURRENT_SOURCE_DIR)
    project(OpenUsdImageExrProbe LANGUAGES CXX)
    enable_testing()
endif()

find_package(OpenEXR 3.1 CONFIG REQUIRED)
add_executable(openusd_image_exr_probe
    image_exr_probe.cpp
    ../../src/image_exr.cpp)
target_compile_features(openusd_image_exr_probe PRIVATE cxx_std_17)
target_compile_definitions(openusd_image_exr_probe PRIVATE
    OPENUSD_DOTNET_BUILD OPENUSD_IMAGE_EXR_TEST_SEAM)
target_include_directories(openusd_image_exr_probe PRIVATE ../../include ../../private)
target_link_libraries(openusd_image_exr_probe PRIVATE OpenEXR::OpenEXR)
if(UNIX)
    find_package(Threads REQUIRED)
    target_link_libraries(openusd_image_exr_probe PRIVATE Threads::Threads)
endif()
if(WIN32)
    target_link_libraries(openusd_image_exr_probe PRIVATE ntdll)
    add_custom_command(TARGET openusd_image_exr_probe POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            $<TARGET_RUNTIME_DLLS:openusd_image_exr_probe>
            $<TARGET_FILE_DIR:openusd_image_exr_probe>
        COMMAND_EXPAND_LISTS)
endif()
if(MSVC)
    target_compile_options(openusd_image_exr_probe PRIVATE /W4 /WX /permissive-)
else()
    target_compile_options(openusd_image_exr_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()
if(TARGET openusd_native_sanitizers)
    target_link_libraries(openusd_image_exr_probe PRIVATE openusd_native_sanitizers)
endif()
foreach(mode roundtrip invalid-input invalid-handles quota cancel faults)
    add_test(NAME openusd_image_exr_${mode}
        COMMAND openusd_image_exr_probe "${CMAKE_CURRENT_BINARY_DIR}" "${mode}")
    set_tests_properties(openusd_image_exr_${mode} PROPERTIES TIMEOUT 60)
endforeach()
if(UNIX)
    add_test(NAME openusd_image_exr_posix-descriptors
        COMMAND openusd_image_exr_probe "${CMAKE_CURRENT_BINARY_DIR}" posix-descriptors)
    set_tests_properties(openusd_image_exr_posix-descriptors PROPERTIES TIMEOUT 60)
endif()
