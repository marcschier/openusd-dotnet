add_executable(openusd_native_probe native_probe.cpp)
target_compile_features(openusd_native_probe PRIVATE cxx_std_17)
target_compile_definitions(openusd_native_probe PRIVATE OPENUSD_DOTNET_BUILD)
target_precompile_headers(openusd_native_probe PRIVATE <algorithm>)
target_link_libraries(openusd_native_probe PRIVATE openusd_dotnet_test usd_m)
if(TARGET openusd_native_sanitizers)
    target_link_libraries(openusd_native_probe PRIVATE openusd_native_sanitizers)
endif()

if(MSVC)
    target_compile_options(openusd_native_probe PRIVATE /W4 /WX /permissive-)
else()
    target_compile_options(openusd_native_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

if(CMAKE_PREFIX_PATH)
    set(_openusd_test_prefixes ${CMAKE_PREFIX_PATH})
    list(GET _openusd_test_prefixes 0 _openusd_test_prefix)
else()
    get_filename_component(
        _openusd_test_prefix
        "${pxr_DIR}/../../.."
        ABSOLUTE)
endif()
add_test(
    NAME openusd_native_probe
    COMMAND
        $<TARGET_FILE:openusd_native_probe>
        "${_openusd_test_prefix}/lib/usd"
        "${CMAKE_SOURCE_DIR}/../test-assets/minimal.usda"
        "${CMAKE_SOURCE_DIR}/../test-assets/mcp-monkey-car-city-textures/asphalt_roughness.jpg"
        "${CMAKE_SOURCE_DIR}/../test-assets/mcp-monkey-car-city-textures/asphalt_diffuse.jpg"
        "${CMAKE_SOURCE_DIR}/../test-assets/native-image-gray-alpha.png"
        "${CMAKE_SOURCE_DIR}/../test-assets/ocio-test-config.ocio")
if(WIN32)
    set_tests_properties(
        openusd_native_probe
        PROPERTIES
            ENVIRONMENT_MODIFICATION
                "PATH=path_list_prepend:${_openusd_test_prefix}/lib;PATH=path_list_prepend:${_openusd_test_prefix}/bin")
elseif(APPLE)
    set_tests_properties(
        openusd_native_probe
        PROPERTIES
            ENVIRONMENT_MODIFICATION
                "DYLD_LIBRARY_PATH=path_list_prepend:${_openusd_test_prefix}/lib")
else()
    set_tests_properties(
        openusd_native_probe
        PROPERTIES
            ENVIRONMENT_MODIFICATION
                "LD_LIBRARY_PATH=path_list_prepend:${_openusd_test_prefix}/lib")
endif()
