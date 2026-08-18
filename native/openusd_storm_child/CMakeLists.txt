set(OPENUSD_STORM_CHILD_SOURCES
    ../include/openusd_render_camera.h
    ../include/openusd_render_pick.h
    ../private/openusd_render_camera_internal.h
    include/openusd_storm_child.h
    src/openusd_storm_child_pick.h
    src/openusd_storm_child_navigation.h)

if(WIN32)
    list(APPEND OPENUSD_STORM_CHILD_SOURCES
        src/openusd_storm_child_internal.h
        src/openusd_storm_child.cpp)
elseif(UNIX AND NOT APPLE)
    list(APPEND OPENUSD_STORM_CHILD_SOURCES
        src/openusd_storm_child_linux.cpp)
elseif(APPLE)
    enable_language(OBJCXX)
    list(APPEND OPENUSD_STORM_CHILD_SOURCES
        src/openusd_storm_child_macos_input.h
        src/openusd_storm_child_macos.mm)
else()
    message(FATAL_ERROR "openusd_storm_child supports Windows, Linux, and macOS only.")
endif()

add_library(openusd_storm_child SHARED ${OPENUSD_STORM_CHILD_SOURCES})
target_compile_features(openusd_storm_child PRIVATE cxx_std_17)
target_compile_definitions(openusd_storm_child PRIVATE OPENUSD_STORM_CHILD_BUILD)
target_include_directories(
    openusd_storm_child
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/../include>
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../private
        ${CMAKE_CURRENT_SOURCE_DIR}/../openusd_dotnet/include
        ${CMAKE_CURRENT_SOURCE_DIR}/../openusd_hydra/include)
target_link_libraries(
    openusd_storm_child
    PRIVATE openusd_hydra openusd_dotnet)
if(WIN32)
    target_link_libraries(openusd_storm_child PRIVATE opengl32 gdi32 user32)
    target_compile_options(
        openusd_storm_child
        PRIVATE /W4 /WX /permissive-)
elseif(UNIX AND NOT APPLE)
    set_target_properties(
        openusd_storm_child
        PROPERTIES
            VERSION 8.0.0
            SOVERSION 8)
    target_link_libraries(
        openusd_storm_child
        PRIVATE OpenGL::GL OpenGL::GLX X11::X11)
    target_compile_options(
        openusd_storm_child
        PRIVATE -Wall -Wextra -Wpedantic -Werror)
elseif(APPLE)
    find_library(APPKIT_FRAMEWORK AppKit REQUIRED)
    find_library(FOUNDATION_FRAMEWORK Foundation REQUIRED)
    target_link_libraries(
        openusd_storm_child
        PRIVATE
            OpenGL::GL
            ${APPKIT_FRAMEWORK}
            ${FOUNDATION_FRAMEWORK})
    target_compile_options(
        openusd_storm_child
        PRIVATE
            -Wall
            -Wextra
            -Wpedantic
            -Werror
            -Wno-deprecated-declarations)
    set_source_files_properties(
        src/openusd_storm_child_macos.mm
        PROPERTIES COMPILE_FLAGS "-fno-objc-arc")
endif()

include(GNUInstallDirs)
install(TARGETS openusd_storm_child
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
    LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}
    ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR})
install(
    FILES include/openusd_storm_child.h
    DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})

if(OPENUSD_BUILD_NATIVE_TESTS)
    enable_testing()
    add_subdirectory(tests)
endif()
