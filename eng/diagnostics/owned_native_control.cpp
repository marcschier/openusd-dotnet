#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <psapi.h>
#include <cwchar>

int wmain(int argc, wchar_t** argv)
{
    if (argc != 2) return 64;
    if (std::wcscmp(argv[1], L"exit7") == 0) return 7;
    if (std::wcscmp(argv[1], L"stall") != 0) return 65;
    PROCESS_MEMORY_COUNTERS_EX memory{};
    memory.cb = sizeof(memory);
    if (!GetProcessMemoryInfo(GetCurrentProcess(),
            reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&memory), sizeof(memory))) return 66;
    HANDLE job = CreateJobObjectW(nullptr, nullptr);
    if (!job) return 67;
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
    limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_PROCESS_MEMORY;
    limits.ProcessMemoryLimit = memory.PrivateUsage + (1u << 20);
    if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, &limits, sizeof(limits)) ||
        !AssignProcessToJobObject(job, GetCurrentProcess())) return 68;
    Sleep(60000);
    CloseHandle(job);
    return 0;
}
