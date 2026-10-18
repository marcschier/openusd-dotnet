"""Native Windows ownership controls; never enumerate or terminate by process name."""
import ctypes
from ctypes import wintypes
import time

kernel = ctypes.WinDLL("kernel32", use_last_error=True)
PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
SYNCHRONIZE = 0x100000


class BasicLimits(ctypes.Structure):
    _fields_ = [
        ("PerProcessUserTimeLimit", ctypes.c_longlong),
        ("PerJobUserTimeLimit", ctypes.c_longlong),
        ("LimitFlags", wintypes.DWORD),
        ("MinimumWorkingSetSize", ctypes.c_size_t),
        ("MaximumWorkingSetSize", ctypes.c_size_t),
        ("ActiveProcessLimit", wintypes.DWORD),
        ("Affinity", ctypes.c_size_t),
        ("PriorityClass", wintypes.DWORD),
        ("SchedulingClass", wintypes.DWORD),
    ]


class IoCounters(ctypes.Structure):
    _fields_ = [(name, ctypes.c_ulonglong) for name in (
        "ReadOperationCount", "WriteOperationCount", "OtherOperationCount",
        "ReadTransferCount", "WriteTransferCount", "OtherTransferCount")]


class ExtendedLimits(ctypes.Structure):
    _fields_ = [("BasicLimitInformation", BasicLimits), ("IoInfo", IoCounters)] + [
        (name, ctypes.c_size_t) for name in (
            "ProcessMemoryLimit", "JobMemoryLimit", "PeakProcessMemoryUsed", "PeakJobMemoryUsed")]


kernel.CreateJobObjectW.argtypes = [ctypes.c_void_p, wintypes.LPCWSTR]
kernel.CreateJobObjectW.restype = wintypes.HANDLE
kernel.SetInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD]
kernel.SetInformationJobObject.restype = wintypes.BOOL
kernel.AssignProcessToJobObject.argtypes = [wintypes.HANDLE, wintypes.HANDLE]
kernel.AssignProcessToJobObject.restype = wintypes.BOOL
kernel.IsProcessInJob.argtypes = [wintypes.HANDLE, wintypes.HANDLE, ctypes.POINTER(wintypes.BOOL)]
kernel.IsProcessInJob.restype = wintypes.BOOL
kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
kernel.OpenProcess.restype = wintypes.HANDLE
kernel.CloseHandle.argtypes = [wintypes.HANDLE]
kernel.CloseHandle.restype = wintypes.BOOL
kernel.QueryFullProcessImageNameW.argtypes = [
    wintypes.HANDLE, wintypes.DWORD, wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
kernel.QueryFullProcessImageNameW.restype = wintypes.BOOL
kernel.GetProcessTimes.argtypes = [
    wintypes.HANDLE, ctypes.POINTER(wintypes.FILETIME), ctypes.POINTER(wintypes.FILETIME),
    ctypes.POINTER(wintypes.FILETIME), ctypes.POINTER(wintypes.FILETIME)]
kernel.GetProcessTimes.restype = wintypes.BOOL
kernel.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
kernel.WaitForSingleObject.restype = wintypes.DWORD
kernel.GetExitCodeProcess.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
kernel.GetExitCodeProcess.restype = wintypes.BOOL
kernel.QueryInformationJobObject.argtypes = [
    wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD)]
kernel.QueryInformationJobObject.restype = wintypes.BOOL
kernel.TerminateJobObject.argtypes = [wintypes.HANDLE, wintypes.UINT]
kernel.TerminateJobObject.restype = wintypes.BOOL


def require(value):
    if not value:
        raise ctypes.WinError(ctypes.get_last_error())


class OwnedJob:
    def __init__(self):
        self.handle = kernel.CreateJobObjectW(None, None)
        require(self.handle)
        limits = ExtendedLimits()
        limits.BasicLimitInformation.LimitFlags = 0x2000
        try:
            require(kernel.SetInformationJobObject(self.handle, 9, ctypes.byref(limits), ctypes.sizeof(limits)))
        except OSError:
            kernel.CloseHandle(self.handle)
            self.handle = None
            raise

    def assign(self, pid):
        process = kernel.OpenProcess(0x100 | 0x1, False, pid)
        require(process)
        try:
            require(kernel.AssignProcessToJobObject(self.handle, process))
        finally:
            require(kernel.CloseHandle(process))

    def members(self):
        capacity = 64
        buffer = ctypes.create_string_buffer(8 + capacity * ctypes.sizeof(ctypes.c_size_t))
        returned = wintypes.DWORD()
        require(kernel.QueryInformationJobObject(
            self.handle, 3, buffer, ctypes.sizeof(buffer), ctypes.byref(returned)))
        header = (wintypes.DWORD * 2).from_buffer(buffer)
        if header[0] > capacity or header[1] > capacity:
            raise RuntimeError("Owned diagnostic job exceeds the bounded process inventory.")
        values = (ctypes.c_size_t * header[1]).from_buffer(buffer, 8)
        return list(values)

    def identity(self, pid):
        handle = kernel.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, False, pid)
        require(handle)
        try:
            belongs = wintypes.BOOL()
            require(kernel.IsProcessInJob(handle, self.handle, ctypes.byref(belongs)))
            if not belongs.value:
                raise RuntimeError("Reported process is not in the owned diagnostic job.")
            length = wintypes.DWORD(32768)
            image = ctypes.create_unicode_buffer(length.value)
            require(kernel.QueryFullProcessImageNameW(handle, 0, image, ctypes.byref(length)))
            times = [wintypes.FILETIME() for _ in range(4)]
            require(kernel.GetProcessTimes(handle, *(ctypes.byref(value) for value in times)))
            started = (times[0].dwHighDateTime << 32) | times[0].dwLowDateTime
            return {"pid": pid, "handle": handle, "image": image.value, "startFileTime": started}
        except (OSError, RuntimeError):
            kernel.CloseHandle(handle)
            raise

    def close(self):
        if self.handle:
            require(kernel.CloseHandle(self.handle))
            self.handle = None

    def stop_and_wait(self, deadline):
        require(kernel.TerminateJobObject(self.handle, 71))
        while self.members():
            if time.monotonic() >= deadline:
                return False
            time.sleep(0.01)
        return True


def exited(identity, milliseconds):
    value = kernel.WaitForSingleObject(identity["handle"], milliseconds)
    if value == 0xFFFFFFFF:
        raise ctypes.WinError(ctypes.get_last_error())
    return value == 0


def exit_code(identity):
    value = wintypes.DWORD()
    require(kernel.GetExitCodeProcess(identity["handle"], ctypes.byref(value)))
    return value.value


def release(identity):
    require(kernel.CloseHandle(identity["handle"]))
