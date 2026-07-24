// Copyright (c) marcschier. Licensed under the MIT License.
#include "context_support.h"

int main()
{
    if (DiagnoseOpenUsdStormLinuxContext(false, false) !=
        OpenUsdStormLinuxContextKind::Missing)
    {
        return 1;
    }
    if (DiagnoseOpenUsdStormLinuxContext(true, true) !=
        OpenUsdStormLinuxContextKind::Glx)
    {
        return 2;
    }
    if (DiagnoseOpenUsdStormLinuxContext(true, false) !=
        OpenUsdStormLinuxContextKind::NonGlx)
    {
        return 3;
    }
    return 0;
}
