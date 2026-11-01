// Copyright (c) marcschier. Licensed under the MIT License.

#include <openvdb/io/File.h>
#include <openvdb/openvdb.h>

#include <array>
#include <filesystem>
#include <iostream>
#include <stdexcept>
#include <string>
#include <utility>

namespace
{

using Path = std::filesystem::path;

openvdb::GridPtrVecPtr ReadGrids(const Path& path)
{
    openvdb::io::File input(path.string());
    input.open(false);
    auto grids = input.getGrids();
    input.close();
    if (!grids || grids->empty())
    {
        throw std::runtime_error("No grids in " + path.string());
    }
    return grids;
}

void WriteGrids(const Path& path, const openvdb::GridPtrVec& grids)
{
    openvdb::io::File output(path.string());
    output.write(grids);
}

void ValidateSourceGrid(const openvdb::GridBase& grid)
{
    const auto bounds = grid.evalActiveVoxelBoundingBox();
    if (bounds.min() != openvdb::Coord(0, 0, 0) ||
        bounds.max() != openvdb::Coord(31, 31, 31) ||
        !grid.transform().isIdentity())
    {
        throw std::runtime_error("Expected an identity-transformed [0,31] grid: " + grid.getName());
    }
}

void WriteUnitFixture(const Path& source, const Path& destination)
{
    auto grids = ReadGrids(source);
    const double scale = 1.0 / 31.0;
    const openvdb::math::Mat4d matrix(
        scale, 0.0, 0.0, 0.0,
        0.0, scale, 0.0, 0.0,
        0.0, 0.0, scale, 0.0,
        -0.5, -0.5, -0.5, 1.0);
    for (const auto& grid : *grids)
    {
        ValidateSourceGrid(*grid);
        grid->setTransform(openvdb::math::Transform::createLinearTransform(matrix));
    }
    WriteGrids(destination, *grids);
}

void WriteOffsetFixture(const Path& source, const Path& destination)
{
    const auto grids = ReadGrids(source);
    if (grids->size() != 1)
    {
        throw std::runtime_error("Expected one density grid in " + source.string());
    }
    const auto original = openvdb::gridPtrCast<openvdb::FloatGrid>(grids->front());
    if (!original)
    {
        throw std::runtime_error("Expected a float density grid in " + source.string());
    }
    ValidateSourceGrid(*original);

    auto shifted = openvdb::FloatGrid::create(original->background());
    shifted->setName("density");
    shifted->setGridClass(original->getGridClass());
    auto sourceAccessor = original->getConstAccessor();
    auto targetAccessor = shifted->getAccessor();
    const openvdb::Coord offset(7, -3, 4);
    for (int z = 0; z < 32; ++z)
    {
        for (int y = 0; y < 32; ++y)
        {
            for (int x = 0; x < 32; ++x)
            {
                const openvdb::Coord coord(x, y, z);
                const float value = sourceAccessor.getValue(coord);
                if (sourceAccessor.isValueOn(coord))
                {
                    targetAccessor.setValueOn(coord + offset, value);
                }
                else if (value != original->background())
                {
                    targetAccessor.setValueOff(coord + offset, value);
                }
            }
        }
    }
    const openvdb::math::Mat4d matrix(
        0.02, 0.006, 0.0, 0.0,
        0.004, -0.015, 0.0, 0.0,
        0.0, 0.0, 0.02, 0.0,
        -0.2, 0.25, 0.02, 1.0);
    shifted->setTransform(openvdb::math::Transform::createLinearTransform(matrix));
    WriteGrids(destination, {shifted});
}

void WriteSingleVoxelFixture(const Path& destination)
{
    auto grid = openvdb::FloatGrid::create(0.0f);
    grid->setName("density");
    grid->getAccessor().setValueOn(openvdb::Coord(3, -2, 5), 1.0f);
    WriteGrids(destination, {grid});
}

void WriteSparseFixture(const Path& destination)
{
    auto grid = openvdb::FloatGrid::create(0.125f);
    grid->setName("density");
    auto values = grid->getAccessor();
    values.setValueOn(openvdb::Coord(-4, -3, -2), 0.0f);
    values.setValueOn(openvdb::Coord(4, 5, 6), 1.0f);
    for (int z = -2; z <= 6; ++z)
    {
        for (int y = 0; y <= 1; ++y)
        {
            for (int x = -1; x <= 1; ++x)
            {
                values.setValueOn(openvdb::Coord(x, y, z), 0.75f);
            }
        }
    }
    values.setValueOff(openvdb::Coord(3, 3, 4), 0.5f);
    const openvdb::math::Mat4d matrix(
        0.125, 0.0, 0.0, 0.0,
        0.0, 0.125, 0.0, 0.0,
        0.0, 0.0, 0.125, 0.0,
        0.0, -0.125, -0.25, 1.0);
    grid->setTransform(openvdb::math::Transform::createLinearTransform(matrix));
    WriteGrids(destination, {grid});
}

} // namespace

int main(int argc, char** argv)
{
    if (argc != 3)
    {
        std::cerr << "Usage: generate_volume_fixtures <source directory> <output directory>\n";
        return 2;
    }
    try
    {
        openvdb::initialize();
        const Path source = argv[1];
        const Path destination = argv[2];
        const std::array<std::pair<const char*, const char*>, 4> files = {{
            {"sampled_density.vdb", "sampled_density_unit.vdb"},
            {"sampled_density_shifted.vdb", "sampled_density_shifted_unit.vdb"},
            {"sampled_density_grid_variants.vdb", "sampled_density_grid_variants_unit.vdb"},
            {"sampled_smoke.vdb", "sampled_smoke_unit.vdb"},
        }};
        for (const auto& [input, output] : files)
        {
            WriteUnitFixture(source / input, destination / output);
        }
        WriteOffsetFixture(
            source / "sampled_density.vdb", destination / "sampled_density_offset_affine.vdb");
        WriteSingleVoxelFixture(destination / "sampled_density_single_voxel.vdb");
        WriteSparseFixture(destination / "sampled_density_sparse.vdb");
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
