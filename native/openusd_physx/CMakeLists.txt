set(OPENUSD_PHYSX_SOURCES
    include/openusd_physx.h
    src/openusd_physx.cpp)

add_library(openusd_physx SHARED ${OPENUSD_PHYSX_SOURCES})
target_compile_features(openusd_physx PRIVATE cxx_std_17)
target_compile_definitions(openusd_physx PRIVATE OPENUSD_PHYSX_BUILD PX_PHYSX_STATIC_LIB)
target_include_directories(openusd_physx
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>)
target_link_libraries(openusd_physx PRIVATE unofficial::omniverse-physx-sdk::sdk usd_m)
if(TARGET openusd_native_sanitizers)
    target_link_libraries(openusd_physx PRIVATE openusd_native_sanitizers)
endif()
if(MSVC)
    target_compile_options(openusd_physx PRIVATE /W4 /WX /permissive-)
else()
    target_compile_options(openusd_physx PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

include(GNUInstallDirs)
install(TARGETS openusd_physx
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
    LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}
    ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR})
install(FILES include/openusd_physx.h DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})