#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <dbghelp.h>
#include <algorithm>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <iostream>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

namespace
{
struct Handle
{
    HANDLE value = nullptr;
    ~Handle() { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); }
    Handle() = default;
    explicit Handle(HANDLE handle) : value(handle) {}
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
};

void Require(bool value)
{
    if (!value) throw std::runtime_error("Owned Win32 diagnostic operation failed.");
}

std::wstring Quote(const std::wstring& value)
{
    std::wstring result = L"\"";
    size_t slashes = 0;
    for (wchar_t ch : value)
    {
        if (ch == L'\\') { ++slashes; continue; }
        result.append(ch == L'"' ? slashes * 2 + 1 : slashes, L'\\');
        result.push_back(ch);
        slashes = 0;
    }
    result.append(slashes * 2, L'\\');
    return result + L"\"";
}

std::string Text(const char* value, size_t maximum)
{
    std::string result = "\"";
    for (size_t index = 0; index < maximum && value[index]; ++index)
    {
        const unsigned char ch = static_cast<unsigned char>(value[index]);
        if (ch == '\\' || ch == '"') result.push_back('\\');
        if (ch < 32 || ch > 126) result += '?';
        else result.push_back(static_cast<char>(ch));
    }
    return result + "\"";
}

void WriteNew(const std::filesystem::path& path, const std::string& value)
{
    Require(value.size() <= 65536);
    const auto temporary = path.wstring() + L".writing";
    {
        Handle file(CreateFileW(temporary.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW,
            FILE_ATTRIBUTE_NORMAL, nullptr));
        Require(file.value != INVALID_HANDLE_VALUE);
        DWORD written = 0;
        Require(WriteFile(file.value, value.data(), static_cast<DWORD>(value.size()), &written, nullptr) &&
            written == value.size());
    }
    Require(MoveFileExW(temporary.c_str(), path.c_str(), 0) != FALSE);
}

uint64_t Started(HANDLE process)
{
    FILETIME creation{}, exit{}, kernel{}, user{};
    Require(GetProcessTimes(process, &creation, &exit, &kernel, &user) != FALSE);
    return (static_cast<uint64_t>(creation.dwHighDateTime) << 32) | creation.dwLowDateTime;
}

struct TrustedSymbols
{
    HMODULE module = LoadLibraryExW(L"dbghelp.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    template<class Function> Function Get(const char* name)
    {
        const auto address = GetProcAddress(module, name);
        Require(address != nullptr);
        Function result{};
        static_assert(sizeof(result) == sizeof(address));
        std::memcpy(&result, &address, sizeof(result));
        return result;
    }
    decltype(&SymSetOptions) options = Get<decltype(&SymSetOptions)>("SymSetOptions");
    decltype(&SymInitializeW) initialize = Get<decltype(&SymInitializeW)>("SymInitializeW");
    decltype(&SymCleanup) cleanup = Get<decltype(&SymCleanup)>("SymCleanup");
    decltype(&StackWalk64) walk = Get<decltype(&StackWalk64)>("StackWalk64");
    decltype(&SymFunctionTableAccess64) functionTable =
        Get<decltype(&SymFunctionTableAccess64)>("SymFunctionTableAccess64");
    decltype(&SymGetModuleBase64) moduleBase = Get<decltype(&SymGetModuleBase64)>("SymGetModuleBase64");
    decltype(&SymGetModuleInfo64) moduleInfo = Get<decltype(&SymGetModuleInfo64)>("SymGetModuleInfo64");
    decltype(&SymFromAddr) symbol = Get<decltype(&SymFromAddr)>("SymFromAddr");
    TrustedSymbols()
    {
        Require(module != nullptr);
        wchar_t actual[MAX_PATH]{}, system[MAX_PATH]{};
        Require(GetModuleFileNameW(module, actual, MAX_PATH) != 0 && GetSystemDirectoryW(system, MAX_PATH) != 0);
        const auto expected = std::filesystem::path(system) / L"dbghelp.dll";
        Require(CompareStringOrdinal(actual, -1, expected.c_str(), -1, TRUE) == CSTR_EQUAL);
    }
    ~TrustedSymbols() { if (module) FreeLibrary(module); }
    TrustedSymbols(const TrustedSymbols&) = delete;
    TrustedSymbols& operator=(const TrustedSymbols&) = delete;
};

std::string Frames(HANDLE process, HANDLE thread, const std::filesystem::path& localSymbols)
{
    Require(SetEnvironmentVariableW(L"_NT_SYMBOL_PATH", L"") != FALSE);
    Require(SetEnvironmentVariableW(L"_NT_ALT_SYMBOL_PATH", L"") != FALSE);
    TrustedSymbols symbols;
    symbols.options(SYMOPT_DEFERRED_LOADS | SYMOPT_UNDNAME | SYMOPT_FAIL_CRITICAL_ERRORS |
        SYMOPT_NO_PROMPTS | SYMOPT_IGNORE_NT_SYMPATH | SYMOPT_DISABLE_SYMSRV_AUTODETECT |
        SYMOPT_IGNORE_CVREC | SYMOPT_PUBLICS_ONLY);
    Require(symbols.initialize(process, localSymbols.c_str(), TRUE) != FALSE);
    const DWORD previous = SuspendThread(thread);
    if (previous == static_cast<DWORD>(-1))
    {
        symbols.cleanup(process);
        throw std::runtime_error("Original main thread could not be suspended.");
    }
    std::ostringstream output;
    try
    {
        CONTEXT context{};
        context.ContextFlags = CONTEXT_FULL;
        Require(GetThreadContext(thread, &context) != FALSE);
        STACKFRAME64 frame{};
        frame.AddrPC.Offset = context.Rip;
        frame.AddrPC.Mode = AddrModeFlat;
        frame.AddrFrame.Offset = context.Rbp;
        frame.AddrFrame.Mode = AddrModeFlat;
        frame.AddrStack.Offset = context.Rsp;
        frame.AddrStack.Mode = AddrModeFlat;
        output << '[';
        size_t count = 0;
        for (; count < 32; ++count)
        {
            if (!symbols.walk(IMAGE_FILE_MACHINE_AMD64, process, thread, &frame, &context,
                    nullptr, symbols.functionTable, symbols.moduleBase, nullptr) || !frame.AddrPC.Offset)
                break;
            if (count) output << ',';
            IMAGEHLP_MODULE64 module{};
            module.SizeOfStruct = sizeof(module);
            const bool moduleKnown = symbols.moduleInfo(process, frame.AddrPC.Offset, &module) != FALSE;
            alignas(SYMBOL_INFO) unsigned char buffer[sizeof(SYMBOL_INFO) + 768]{};
            auto* symbol = reinterpret_cast<SYMBOL_INFO*>(buffer);
            symbol->SizeOfStruct = sizeof(SYMBOL_INFO);
            symbol->MaxNameLen = 768;
            DWORD64 displacement = 0;
            const bool symbolKnown = symbols.symbol(process, frame.AddrPC.Offset, &displacement, symbol) != FALSE;
            output << "{\"pc\":\"0x" << std::hex << frame.AddrPC.Offset << std::dec << "\",\"module\":";
            output << (moduleKnown ? Text(module.ModuleName, sizeof(module.ModuleName)) : "null");
            output << ",\"moduleOffset\":";
            if (moduleKnown) output << (frame.AddrPC.Offset - module.BaseOfImage);
            else output << "null";
            output << ",\"function\":" << (symbolKnown ? Text(symbol->Name, 767) : "null") << '}';
        }
        output << ']';
        Require(count != 0);
    }
    catch (const std::exception&)
    {
        ResumeThread(thread);
        symbols.cleanup(process);
        throw;
    }
    Require(ResumeThread(thread) != static_cast<DWORD>(-1));
    Require(symbols.cleanup(process) != FALSE);
    return output.str();
}
}

int wmain(int argc, wchar_t** argv)
{
    if (argc < 5 || sizeof(void*) != 8) return 64;
    try
    {
        const std::filesystem::path output(argv[1]);
        const std::filesystem::path working(argv[2]);
        const std::filesystem::path executable(argv[3]);
        Require(output.is_absolute() && working.is_absolute() && executable.is_absolute());
        Require(std::filesystem::is_directory(output) && std::filesystem::is_regular_file(executable));
        std::wstring command = Quote(executable.wstring());
        for (int index = 4; index < argc; ++index) command += L" " + Quote(argv[index]);
        char gate = 0;
        std::cin >> gate;
        Require(gate == 'G');

        Handle job(CreateJobObjectW(nullptr, nullptr));
        Require(job.value != nullptr);
        JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
        limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        Require(SetInformationJobObject(job.value, JobObjectExtendedLimitInformation,
            &limits, sizeof(limits)) != FALSE);
        Handle nullFile(CreateFileW(L"NUL", GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr));
        Require(nullFile.value != INVALID_HANDLE_VALUE);
        Require(SetHandleInformation(nullFile.value, HANDLE_FLAG_INHERIT, HANDLE_FLAG_INHERIT) != FALSE);
        STARTUPINFOW startup{};
        startup.cb = sizeof(startup);
        startup.dwFlags = STARTF_USESTDHANDLES;
        startup.hStdInput = startup.hStdOutput = startup.hStdError = nullFile.value;
        PROCESS_INFORMATION info{};
        Require(CreateProcessW(executable.c_str(), command.data(), nullptr, nullptr, TRUE,
            CREATE_SUSPENDED | CREATE_NO_WINDOW, nullptr, working.c_str(), &startup, &info) != FALSE);
        Handle process(info.hProcess), mainThread(info.hThread);
        if (!AssignProcessToJobObject(job.value, process.value))
        {
            TerminateProcess(process.value, 71);
            WaitForSingleObject(process.value, 1000);
            throw std::runtime_error("The original suspended subject could not enter its owned job.");
        }
        const auto began = GetTickCount64();
        std::ostringstream identity;
        identity << "{\"pid\":" << info.dwProcessId << ",\"mainThreadId\":" << info.dwThreadId
            << ",\"startFileTime\":" << Started(process.value) << "}\n";
        WriteNew(output / "subject-identity.json", identity.str());
        char resume = 0;
        std::cin >> resume;
        Require(resume == 'R');
        Require(ResumeThread(mainThread.value) != static_cast<DWORD>(-1));
        const DWORD state = WaitForSingleObject(process.value, 30000);
        Require(state == WAIT_OBJECT_0 || state == WAIT_TIMEOUT);
        std::ostringstream result;
        result << "{\"schemaVersion\":1,\"diagnosticOnly\":true,\"pid\":" << info.dwProcessId
            << ",\"mainThreadId\":" << info.dwThreadId;
        if (state == WAIT_OBJECT_0)
        {
            DWORD exit = 0;
            Require(GetExitCodeProcess(process.value, &exit) != FALSE);
            result << ",\"kind\":\"exited-before-stall\",\"numericExitCode\":" << exit;
        }
        else
        {
            result << ",\"kind\":\"original-main-stall\",\"frames\":"
                << Frames(process.value, mainThread.value, executable.parent_path());
        }
        Require(TerminateJobObject(job.value, 71) != FALSE);
        Require(WaitForSingleObject(process.value, 1000) == WAIT_OBJECT_0);
        const auto elapsed = GetTickCount64() - began;
        result << ",\"ownedSubjectExitMilliseconds\":" << elapsed
            << ",\"trustedSystem32DbgHelpLocalOnly\":true"
            << ",\"noArgumentsLocalsRegistersRawStackOtherThreadsOrDump\":true}\n";
        WriteNew(output / "result.json", result.str());
        return elapsed <= 45000 ? 0 : 72;
    }
    catch (const std::exception&)
    {
        std::cerr << "WINDOWS_MAIN_STALL_DIAGNOSTIC_UNAVAILABLE win32=" << GetLastError() << '\n';
        return 70;
    }
}
