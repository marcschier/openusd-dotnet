cmake_minimum_required(VERSION 3.24)
project(openusd_cesium_probe LANGUAGES CXX)

find_package(cesium-native CONFIG REQUIRED)

add_executable(openusd_cesium_probe
    main.cpp
)

target_compile_features(openusd_cesium_probe PRIVATE cxx_std_20)
target_link_libraries(openusd_cesium_probe PRIVATE
    Cesium3DTilesReader
)

include(CTest)
add_test(NAME openusd_cesium_tileset_probe COMMAND openusd_cesium_probe)
