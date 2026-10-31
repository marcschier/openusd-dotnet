// Copyright (c) marcschier. Licensed under the MIT License.

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <psapi.h>
#include <cstdio>
#include <cwchar>

namespace
{
constexpr SIZE_T BlockBytes = 8u * 1024u * 1024u;

SIZE_T PrivateCommit(HANDLE process)
{
    PROCESS_MEMORY_COUNTERS_EX counters{};
    counters.cb = sizeof(counters);
    if (!GetProcessMemoryInfo(process, reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&counters), sizeof(counters)))
    {
        std::fprintf(stderr, "GetProcessMemoryInfo failed: %lu\n", GetLastError());
        ExitProcess(2);
    }
    return counters.PrivateUsage;
}
}

int wmain(int argc, wchar_t** argv)
{
    if (argc == 2)
    {
        const DWORD parent_id = static_cast<DWORD>(std::wcstoul(argv[1], nullptr, 10));
        HANDLE parent = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, FALSE, parent_id);
        if (!parent) { return 2; }
        void* blocks[32]{};
        size_t count = 0;
        SIZE_T peak = 0;
        DWORD failure = 0;
        for (; count < 32; ++count)
        {
            blocks[count] = VirtualAlloc(nullptr, BlockBytes, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
            if (!blocks[count])
            {
                failure = GetLastError();
                break;
            }
            auto* bytes = static_cast<volatile unsigned char*>(blocks[count]);
            for (SIZE_T offset = 0; offset < BlockBytes; offset += 4096) { bytes[offset] = 1; }
            const SIZE_T live = PrivateCommit(GetCurrentProcess()) + PrivateCommit(parent);
            if (live > peak) { peak = live; }
        }
        std::printf("{\"refused\":%s,\"allocatedBytes\":%zu,\"peakLivePrivateBytes\":%zu,\"error\":%lu}\n",
            failure ? "true" : "false", count * BlockBytes, peak, failure);
        for (size_t index = 0; index < count; ++index) { VirtualFree(blocks[index], 0, MEM_RELEASE); }
        CloseHandle(parent);
        return failure ? 0 : 3;
    }
    void* retained = VirtualAlloc(nullptr, 4 * BlockBytes, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
    if (!retained) { return 2; }
    wchar_t path[32768]{};
    if (!GetModuleFileNameW(nullptr, path, 32768)) { return 2; }
    wchar_t command[33000]{};
    if (swprintf_s(command, L"\"%s\" %lu", path, GetCurrentProcessId()) < 0) { return 2; }
    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESTDHANDLES;
    startup.hStdInput = GetStdHandle(STD_INPUT_HANDLE);
    startup.hStdOutput = GetStdHandle(STD_OUTPUT_HANDLE);
    startup.hStdError = GetStdHandle(STD_ERROR_HANDLE);
    PROCESS_INFORMATION child{};
    if (!CreateProcessW(path, command, nullptr, nullptr, TRUE, CREATE_NO_WINDOW,
        nullptr, nullptr, &startup, &child)) { return 2; }
    CloseHandle(child.hThread);
    if (WaitForSingleObject(child.hProcess, 20000) != WAIT_OBJECT_0)
    {
        TerminateProcess(child.hProcess, 2);
        CloseHandle(child.hProcess);
        return 2;
    }
    DWORD code = 2;
    if (!GetExitCodeProcess(child.hProcess, &code)) { return 2; }
    CloseHandle(child.hProcess);
    VirtualFree(retained, 0, MEM_RELEASE);
    return static_cast<int>(code);
}
