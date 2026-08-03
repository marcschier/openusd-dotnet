#include <Cesium3DTiles/Tile.h>
#include <Cesium3DTiles/Tileset.h>
#include <Cesium3DTilesReader/TilesetReader.h>

#include <cmath>
#include <cstddef>
#include <iostream>
#include <span>
#include <string_view>
#include <vector>

namespace {

std::size_t countTiles(const Cesium3DTiles::Tile& tile) {
  std::size_t count = 1;
  for (const Cesium3DTiles::Tile& child : tile.children) {
    count += countTiles(child);
  }

  return count;
}

} // namespace

int main() {
  constexpr std::string_view tilesetJson = R"json({
    "asset": { "version": "1.1" },
    "geometricError": 500.0,
    "root": {
      "boundingVolume": { "box": [0, 0, 0, 10, 0, 0, 0, 10, 0, 0, 0, 10] },
      "geometricError": 250.0,
      "refine": "ADD",
      "children": [
        {
          "boundingVolume": { "sphere": [0, 0, 0, 5] },
          "geometricError": 0.0,
          "content": { "uri": "0.b3dm" }
        },
        {
          "boundingVolume": { "sphere": [1, 1, 1, 3] },
          "geometricError": 0.0,
          "content": { "uri": "1.b3dm" }
        }
      ]
    }
  })json";

  const auto* begin = reinterpret_cast<const std::byte*>(tilesetJson.data());
  std::span<const std::byte> data(begin, tilesetJson.size());

  Cesium3DTilesReader::TilesetReader reader;
  auto result = reader.readFromJson(data);
  if (!result.value) {
    std::cerr << "Tileset parse failed with " << result.errors.size() << " error(s).\n";
    for (const auto& error : result.errors) {
      std::cerr << error << '\n';
    }

    return 1;
  }

  const Cesium3DTiles::Tileset& tileset = *result.value;
  const std::size_t tileCount = countTiles(tileset.root);
  if (tileset.asset.version != "1.1" || std::abs(tileset.geometricError - 500.0) > 0.0001 ||
      tileCount != 3) {
    std::cerr << "Unexpected parsed tileset: version=" << tileset.asset.version
              << ", geometricError=" << tileset.geometricError << ", tileCount=" << tileCount
              << '\n';
    return 2;
  }

  std::cout << "Parsed cesium-native tileset: tileCount=" << tileCount
            << ", geometricError=" << tileset.geometricError << '\n';
  return 0;
}
