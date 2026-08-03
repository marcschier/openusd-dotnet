set(OPENUSD_CESIUM_SOURCES
    include/openusd_cesium.h
    src/openusd_cesium.cpp)

add_library(openusd_cesium SHARED ${OPENUSD_CESIUM_SOURCES})
target_compile_features(openusd_cesium PRIVATE cxx_std_17)
target_compile_definitions(openusd_cesium PRIVATE OPENUSD_CESIUM_BUILD)
target_include_directories(openusd_cesium
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>)
target_link_libraries(openusd_cesium PRIVATE Cesium3DTilesSelection Cesium3DTilesContent)
if(TARGET openusd_native_sanitizers)
    target_link_libraries(openusd_cesium PRIVATE openusd_native_sanitizers)
endif()
if(MSVC)
    target_compile_options(openusd_cesium PRIVATE /W4 /WX /permissive- /wd4996)
else()
    target_compile_options(openusd_cesium PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

include(GNUInstallDirs)
install(TARGETS openusd_cesium
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
    LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}
    ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR})
install(FILES include/openusd_cesium.h DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})