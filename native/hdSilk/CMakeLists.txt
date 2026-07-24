set(OPENUSD_HDSILK_SOURCES
    ../include/openusd_render_camera.h
    include/openusd_hdsilk.h
    ../private/openusd_render_camera_internal.h
    src/api.cpp
    src/implicitSurfaceSceneIndexPlugin.h
    src/implicitSurfaceSceneIndexPlugin.cpp
    src/mesh.h
    src/mesh.cpp
    src/renderDelegate.h
    src/renderDelegate.cpp
    src/rendererPlugin.h
    src/rendererPlugin.cpp
    src/renderPass.h
    src/renderPass.cpp
    src/sceneState.h
    src/sceneState.cpp)

function(configure_openusd_hdsilk_target target_name core_target)
    target_compile_features(${target_name} PRIVATE cxx_std_17)
    target_compile_definitions(${target_name} PRIVATE OPENUSD_HDSILK_BUILD)
    target_include_directories(${target_name}
        PUBLIC
            $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/../include>
            $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
            $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/../openusd_dotnet/include>
            $<INSTALL_INTERFACE:include>
        PRIVATE
            ${CMAKE_CURRENT_SOURCE_DIR}/src
            ${CMAKE_CURRENT_SOURCE_DIR}/../private
            ${CMAKE_CURRENT_SOURCE_DIR}/../openusd_dotnet/private)
    target_link_libraries(${target_name} PRIVATE ${core_target} usd_m)
    if(MSVC)
        target_compile_options(${target_name} PRIVATE /W4 /WX /permissive-)
    else()
        target_compile_options(${target_name} PRIVATE -Wall -Wextra -Wpedantic -Werror)
    endif()
endfunction()

add_library(openusd_hdsilk SHARED ${OPENUSD_HDSILK_SOURCES})
configure_openusd_hdsilk_target(openusd_hdsilk openusd_dotnet)

# The plugInfo.json LibraryPath is relative to the plugin root
# (<prefix>/plugin/usd/hdSilk, per "Root": ".."), so it must walk back up to
# the shim install prefix before descending into the directory that holds
# the built shared library.
set(HDSILK_LIBRARY_FILE_NAME "${CMAKE_SHARED_LIBRARY_PREFIX}openusd_hdsilk${CMAKE_SHARED_LIBRARY_SUFFIX}")
if(WIN32)
    set(HDSILK_LIBRARY_RELATIVE_DIR "bin")
else()
    set(HDSILK_LIBRARY_RELATIVE_DIR "lib")
endif()
set(HDSILK_PLUGIN_LIBRARY_PATH "../../../${HDSILK_LIBRARY_RELATIVE_DIR}/${HDSILK_LIBRARY_FILE_NAME}")

set(HDSILK_CONFIGURED_PLUGINFO ${CMAKE_CURRENT_BINARY_DIR}/resources/plugInfo.json)
configure_file(
    resources/plugInfo.json.in
    ${HDSILK_CONFIGURED_PLUGINFO}
    @ONLY)

include(GNUInstallDirs)
install(TARGETS openusd_hdsilk
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
    LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}
    ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR})
install(FILES include/openusd_hdsilk.h DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
install(FILES ${HDSILK_CONFIGURED_PLUGINFO} DESTINATION plugin/usd/hdSilk/resources)

option(OPENUSD_HDSILK_BUILD_TESTS "Build the hdSilk native probe" ON)
if(OPENUSD_HDSILK_BUILD_TESTS)
    add_library(openusd_hdsilk_test SHARED ${OPENUSD_HDSILK_SOURCES})
    configure_openusd_hdsilk_target(
        openusd_hdsilk_test
        openusd_dotnet_renderer_test)
    target_compile_definitions(
        openusd_hdsilk_test
        PRIVATE
            OPENUSD_HDSILK_ENABLE_TEST_HOOKS
            OPENUSD_RENDERER_ENABLE_TEST_HOOKS)

    set(HDSILK_TEST_LIBRARY_FILE_NAME
        "${CMAKE_SHARED_LIBRARY_PREFIX}openusd_hdsilk_test${CMAKE_SHARED_LIBRARY_SUFFIX}")
    set(HDSILK_PLUGIN_LIBRARY_PATH
        "../../../${HDSILK_LIBRARY_RELATIVE_DIR}/${HDSILK_TEST_LIBRARY_FILE_NAME}")
    set(HDSILK_TEST_CONFIGURED_PLUGINFO
        ${CMAKE_CURRENT_BINARY_DIR}/test-resources/plugInfo.json)
    configure_file(
        resources/plugInfo.json.in
        ${HDSILK_TEST_CONFIGURED_PLUGINFO}
        @ONLY)

    enable_testing()
    add_subdirectory(tests)
endif()
