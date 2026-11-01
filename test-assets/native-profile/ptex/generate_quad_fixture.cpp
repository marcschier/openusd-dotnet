// Copyright (c) marcschier. Licensed under the MIT License.

#include <Ptexture.h>

#include <array>
#include <cstdint>
#include <iostream>

int main(int argc, char** argv)
{
    if (argc != 2)
    {
        std::cerr << "Usage: generate_quad_fixture <output.ptx>\n";
        return 1;
    }

    Ptex::String error;
    Ptex::PtexPtr<Ptex::PtexWriter> writer(Ptex::PtexWriter::open(
        argv[1], Ptex::mt_quad, Ptex::dt_uint8, 3, -1, 4, error));
    if (!writer)
    {
        std::cerr << "Could not open Ptex output: " << error.c_str() << '\n';
        return 1;
    }

    constexpr std::array<std::array<std::uint8_t, 3>, 4> colors = {{
        {{255, 255, 255}},
        {{255, 0, 0}},
        {{0, 255, 0}},
        {{0, 0, 255}},
    }};
    const Ptex::FaceInfo face(Ptex::Res(2, 2));
    for (int index = 0; index < static_cast<int>(colors.size()); ++index)
    {
        if (!writer->writeConstantFace(index, face, colors[index].data()))
        {
            std::cerr << "Could not write Ptex face " << index << '\n';
            return 1;
        }
    }

    if (!writer->close(error))
    {
        std::cerr << "Could not close Ptex output: " << error.c_str() << '\n';
        return 1;
    }
    return 0;
}
