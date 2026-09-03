set(OPENUSD_MDL_SOURCES
    ../include/openusd_mdl.h
    src/adapter.cpp
    src/sdk_backend.h
    src/sdk_backend.cpp)

function(configure_openusd_mdl_target target_name)
    target_compile_features(${target_name} PRIVATE cxx_std_17)
    target_compile_definitions(${target_name} PRIVATE OPENUSD_MDL_BUILD)
    target_include_directories(${target_name}
        PUBLIC
            $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/../include>
            $<INSTALL_INTERFACE:include>)
    if(MSVC)
        target_compile_options(${target_name} PRIVATE /W4 /WX /permissive-)
    else()
        target_compile_options(${target_name} PRIVATE -Wall -Wextra -Wpedantic -Werror)
        set_target_properties(${target_name} PROPERTIES CXX_VISIBILITY_PRESET hidden)
    endif()
endfunction()

add_library(openusd_mdl SHARED ${OPENUSD_MDL_SOURCES})
configure_openusd_mdl_target(openusd_mdl)

# The dependency-free adapter deliberately links nothing: not OpenUSD, not
# MaterialX, and not an MDL SDK. Keeping the link line empty is what makes "no
# MDL or NVIDIA binary reaches a base package" checkable by inspecting the built
# artifact rather than by trusting the build description.
#
# OPENUSD_MDL_SDK_ROOT adds a *second* target beside it rather than changing it.
# Two artifacts behind one ABI is what lets an operator deploy either without
# rebuilding hdSilk, and it keeps the SDK-free adapter provably SDK-free even in
# a tree where the SDK headers were fetched.
if(OPENUSD_MDL_SDK_ROOT)
    if(NOT EXISTS "${OPENUSD_MDL_SDK_ROOT}/include/mi/neuraylib/ineuray.h")
        message(
            FATAL_ERROR
            "OPENUSD_MDL_SDK_ROOT='${OPENUSD_MDL_SDK_ROOT}' has no "
            "include/mi/neuraylib/ineuray.h. Run ./eng/fetch-mdl-sdk.ps1 first.")
    endif()

    add_library(openusd_mdl_sdk SHARED ${OPENUSD_MDL_SOURCES})
    configure_openusd_mdl_target(openusd_mdl_sdk)
    target_compile_definitions(openusd_mdl_sdk PRIVATE OPENUSD_MDL_WITH_SDK)
    # The neuraylib headers are third-party and are not held to this project's
    # warning level, exactly as the OpenUSD headers are not.
    if(MSVC)
        target_compile_options(
            openusd_mdl_sdk
            PRIVATE "/external:I${OPENUSD_MDL_SDK_ROOT}/include" /external:W0)
        target_include_directories(
            openusd_mdl_sdk PRIVATE "${OPENUSD_MDL_SDK_ROOT}/include")
    else()
        target_include_directories(
            openusd_mdl_sdk SYSTEM PRIVATE "${OPENUSD_MDL_SDK_ROOT}/include")
    endif()

    # Still no MDL link line. neuraylib is a header-only interface layer, and the
    # runtime is opened through mi_factory at run time from a location the
    # operator supplies, so no MDL SDK code is built into this artifact and none
    # of it can be redistributed by accident.
    if(NOT WIN32)
        target_link_libraries(openusd_mdl_sdk PRIVATE ${CMAKE_DL_LIBS})
    endif()
endif()

include(GNUInstallDirs)
install(TARGETS openusd_mdl
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
    LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}
    ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR})
install(FILES ../include/openusd_mdl.h DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})

if(OPENUSD_BUILD_NATIVE_TESTS)
    enable_testing()
    add_subdirectory(tests)
endif()
