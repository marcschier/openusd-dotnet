add_executable(openusd_native_probe native_probe.cpp)
target_compile_features(openusd_native_probe PRIVATE cxx_std_17)
target_compile_definitions(openusd_native_probe PRIVATE OPENUSD_DOTNET_BUILD)
target_precompile_headers(openusd_native_probe PRIVATE <algorithm>)
target_link_libraries(openusd_native_probe PRIVATE openusd_dotnet_test usd_m)
add_executable(
    openusd_render_specification_probe
    ../openusd_dotnet/tests/render_specification_probe.cpp)
target_compile_features(openusd_render_specification_probe PRIVATE cxx_std_17)
target_link_libraries(openusd_render_specification_probe PRIVATE openusd_dotnet usd_m)
add_executable(
    openusd_render_admission_probe
    ../openusd_dotnet/tests/render_admission_probe.cpp)
target_compile_features(openusd_render_admission_probe PRIVATE cxx_std_17)
target_link_libraries(openusd_render_admission_probe PRIVATE openusd_dotnet usd_m)
if(WIN32)
    target_link_libraries(openusd_render_admission_probe PRIVATE psapi)
endif()
if(TARGET openusd_native_sanitizers)
    target_link_libraries(openusd_native_probe PRIVATE openusd_native_sanitizers)
    target_link_libraries(openusd_render_specification_probe PRIVATE openusd_native_sanitizers)
    target_link_libraries(openusd_render_admission_probe PRIVATE openusd_native_sanitizers)
endif()

# The third-party resolver contract is proved with a real out-of-tree plugin tree rather than a
# linked stub: the plugin is loaded only through PlugRegistry, keeps its own unflattened
# plugInfo.json under its own resources directory, and is never linked into the shim or installed.
set(OPENUSD_TEST_RESOLVER_TREE "${CMAKE_CURRENT_BINARY_DIR}/plugins/openusdTestResolver")
add_library(openusd_test_resolver MODULE resolver_plugin/testResolver.cpp)
target_compile_features(openusd_test_resolver PRIVATE cxx_std_17)
target_link_libraries(openusd_test_resolver PRIVATE usd_m)
set_target_properties(
    openusd_test_resolver
    PROPERTIES
        RUNTIME_OUTPUT_DIRECTORY "${OPENUSD_TEST_RESOLVER_TREE}"
        LIBRARY_OUTPUT_DIRECTORY "${OPENUSD_TEST_RESOLVER_TREE}")
if(MSVC)
    target_compile_options(openusd_test_resolver PRIVATE /W4 /WX /permissive-)
else()
    target_compile_options(openusd_test_resolver PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()
file(
    GENERATE
    OUTPUT "${OPENUSD_TEST_RESOLVER_TREE}/resources/plugInfo.json"
    INPUT "${CMAKE_CURRENT_SOURCE_DIR}/resolver_plugin/plugInfo.json.in")
add_dependencies(openusd_native_probe openusd_test_resolver)

if(MSVC)
    target_compile_options(openusd_native_probe PRIVATE /W4 /WX /permissive-)
    target_compile_options(openusd_render_specification_probe PRIVATE /W4 /WX /permissive-)
    target_compile_options(openusd_render_admission_probe PRIVATE /W4 /WX /permissive-)
else()
    target_compile_options(openusd_native_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
    target_compile_options(openusd_render_specification_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
    target_compile_options(openusd_render_admission_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
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

# A prefix that reaches this file with surrounding whitespace or a leftover
# quote pair -- a configure line such as -DCMAKE_PREFIX_PATH='<path> ' produces
# both -- is not a cosmetic problem. Every path below is built by concatenation,
# so one trailing space turns "<prefix>/lib" into a directory that does not
# exist, the probe's dependent libraries are never found, and on Windows the
# loader failure is a modal error box that hangs the test instead of failing it.
# The normalization removes only what a shell would have consumed; a quote
# inside the path is part of the path. See cmake/OpenUsdNormalizePrefixPath.cmake.
include(${CMAKE_CURRENT_SOURCE_DIR}/../cmake/OpenUsdNormalizePrefixPath.cmake)
openusd_normalize_prefix_path(_openusd_test_prefix)
get_filename_component(_openusd_test_prefix "${_openusd_test_prefix}" ABSOLUTE)
if(NOT EXISTS "${_openusd_test_prefix}/lib/usd")
    message(
        FATAL_ERROR
        "The native probe needs the OpenUSD plugin registry at "
        "'${_openusd_test_prefix}/lib/usd'. Point CMAKE_PREFIX_PATH at the "
        "install prefix that owns it.")
endif()

include("${CMAKE_CURRENT_SOURCE_DIR}/../openusd_dotnet/tests/hierarchy_probe.cmake")
include("${CMAKE_CURRENT_SOURCE_DIR}/warehouse_audit_probe.cmake")
include("${CMAKE_CURRENT_SOURCE_DIR}/../openusd_dotnet/tests/property_inspection_probe.cmake")
include("${CMAKE_CURRENT_SOURCE_DIR}/../openusd_dotnet/tests/render_deferred_probe.cmake")
openusd_add_render_deferred_probes("${_openusd_test_prefix}")

# The normalization is gated in its own right, through "cmake -P" so the test
# runs the same function the configure step just ran.
add_test(
    NAME openusd_prefix_path_contract
    COMMAND
        ${CMAKE_COMMAND}
        -P
        "${CMAKE_CURRENT_SOURCE_DIR}/normalize-prefix-path-contract.cmake")
add_test(
    NAME openusd_native_probe
    COMMAND
        $<TARGET_FILE:openusd_native_probe>
        "${_openusd_test_prefix}/lib/usd"
        "${CMAKE_SOURCE_DIR}/../test-assets/minimal.usda"
        "${CMAKE_SOURCE_DIR}/../test-assets/mcp-monkey-car-city-textures/asphalt_roughness.jpg"
        "${CMAKE_SOURCE_DIR}/../test-assets/mcp-monkey-car-city-textures/asphalt_diffuse.jpg"
        "${CMAKE_SOURCE_DIR}/../test-assets/native-image-gray-alpha.png"
        "${CMAKE_SOURCE_DIR}/../test-assets/ocio-test-config.ocio"
        "${OPENUSD_TEST_RESOLVER_TREE}/resources/plugInfo.json")
add_test(
    NAME openusd_render_specification_probe
    COMMAND
        $<TARGET_FILE:openusd_render_specification_probe>
        "${_openusd_test_prefix}/lib/usd"
        "${CMAKE_CURRENT_BINARY_DIR}/render-specification-probe.usda")
set(_openusd_render_admission_tests)
foreach(_case expression cstring properties property-order targets targets-deleted targets-ordered walk purpose-edit
    source-site source-site-cached source-site-shared source-variant-set source-variant-selection dual-role)
    set(_name "openusd_render_admission_${_case}")
    add_test(
        NAME "${_name}"
        COMMAND $<TARGET_FILE:openusd_render_admission_probe>
            "${_openusd_test_prefix}/lib/usd"
            "${CMAKE_CURRENT_BINARY_DIR}/render-admission-${_case}.usda"
            "${_case}")
    list(APPEND _openusd_render_admission_tests "${_name}")
endforeach()
set_tests_properties(${_openusd_render_admission_tests} PROPERTIES TIMEOUT 120)
if(WIN32)
    set_tests_properties(
        openusd_native_probe openusd_render_specification_probe ${_openusd_render_admission_tests}
        PROPERTIES
            ENVIRONMENT_MODIFICATION
                "PATH=path_list_prepend:${_openusd_test_prefix}/lib;PATH=path_list_prepend:${_openusd_test_prefix}/bin;PATH=path_list_prepend:$<TARGET_FILE_DIR:openusd_dotnet>")
elseif(APPLE)
    set_tests_properties(
        openusd_native_probe openusd_render_specification_probe ${_openusd_render_admission_tests}
        PROPERTIES
            ENVIRONMENT_MODIFICATION
                "DYLD_LIBRARY_PATH=path_list_prepend:${_openusd_test_prefix}/lib")
else()
    set_tests_properties(
        openusd_native_probe openusd_render_specification_probe ${_openusd_render_admission_tests}
        PROPERTIES
            ENVIRONMENT_MODIFICATION
                "LD_LIBRARY_PATH=path_list_prepend:${_openusd_test_prefix}/lib")
endif()

include("${CMAKE_CURRENT_SOURCE_DIR}/../openusd_dotnet/tests/layer_edit_probe.cmake")
include("${CMAKE_CURRENT_SOURCE_DIR}/../openusd_dotnet/tests/review_cold_inspection_probe.cmake")
openusd_add_cold_inspection_probe("${_openusd_test_prefix}")
