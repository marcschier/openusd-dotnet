set(OPENUSD_HYDRA_SOURCES
    ../include/openusd_render_camera.h
    ../include/openusd_render_pick.h
    include/openusd_hydra.h
    ../private/openusd_render_camera_internal.h
    src/openusd_render_pick_internal.h
    src/openusd_hydra.cpp)

function(configure_openusd_hydra_target target_name core_target)
    target_compile_features(${target_name} PRIVATE cxx_std_17)
    target_compile_definitions(${target_name} PRIVATE OPENUSD_HYDRA_BUILD)
    target_include_directories(${target_name}
        PUBLIC
            $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/../include>
            $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
            $<INSTALL_INTERFACE:include>
        PRIVATE
            ${CMAKE_CURRENT_SOURCE_DIR}/../openusd_dotnet/include
            ${CMAKE_CURRENT_SOURCE_DIR}/../openusd_dotnet/private
            ${CMAKE_CURRENT_SOURCE_DIR}/../private
            ${CMAKE_CURRENT_SOURCE_DIR}/src)
    target_link_libraries(${target_name} PRIVATE ${core_target} usd_m)
    if(MSVC)
        target_compile_options(${target_name} PRIVATE /W4 /WX /permissive-)
    else()
        target_compile_options(${target_name} PRIVATE -Wall -Wextra -Wpedantic -Werror)
    endif()
endfunction()

add_library(openusd_hydra SHARED ${OPENUSD_HYDRA_SOURCES})
configure_openusd_hydra_target(openusd_hydra openusd_dotnet)

include(GNUInstallDirs)
install(TARGETS openusd_hydra
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
    LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}
    ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR})
install(FILES include/openusd_hydra.h DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})

option(OPENUSD_HYDRA_BUILD_TESTS "Build the Storm context diagnostic probe" ON)
if(OPENUSD_HYDRA_BUILD_TESTS)
    add_library(openusd_hydra_test STATIC ${OPENUSD_HYDRA_SOURCES})
    configure_openusd_hydra_target(
        openusd_hydra_test
        openusd_dotnet_renderer_test)
    target_compile_definitions(
        openusd_hydra_test
        PRIVATE OPENUSD_RENDERER_ENABLE_TEST_HOOKS)
    enable_testing()
    add_subdirectory(tests)
endif()
