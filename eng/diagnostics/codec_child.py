"""Private valid-input codec control; emits only fixed provider metadata, never values or frames."""
import ctypes
import json
import os
from pathlib import Path
import resource
import sys

resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
root = Path(sys.argv[1])
case = sys.argv[2]
global_mode = os.RTLD_NOW | os.RTLD_GLOBAL
zstd_path = root / "libzstd.so.1"
if case == "host-alone":
    zstd_path = Path("/lib/x86_64-linux-gnu/libzstd.so.1")
if case.startswith("blosc"):
    ctypes.CDLL(str(root / "libblosc.so.1"), mode=global_mode)
elif case.startswith("system"):
    ctypes.CDLL("/lib/x86_64-linux-gnu/libzstd.so.1", mode=global_mode)
if case.endswith("deep") and not hasattr(os, "RTLD_DEEPBIND"):
    raise RuntimeError("Diagnostic-only deep-binding control is unavailable.")
mode = global_mode | (os.RTLD_DEEPBIND if case.endswith("deep") else 0)
zstd = ctypes.CDLL(str(zstd_path), mode=mode)
default = ctypes.CDLL(None)


class DlInfo(ctypes.Structure):
    _fields_ = [("file", ctypes.c_char_p), ("base", ctypes.c_void_p),
                ("symbol", ctypes.c_char_p), ("address", ctypes.c_void_p)]


dl = ctypes.CDLL("libdl.so.2")
dl.dladdr.argtypes = [ctypes.c_void_p, ctypes.POINTER(DlInfo)]
dl.dladdr.restype = ctypes.c_int


def provider(library, name):
    try:
        function = getattr(library, name)
    except AttributeError:
        return None
    info = DlInfo()
    if not dl.dladdr(ctypes.cast(function, ctypes.c_void_p), ctypes.byref(info)):
        raise RuntimeError("Owned codec provider metadata unavailable.")
    return Path(info.file.decode()).name


names = ("ZSTD_compress", "ZSTD_compressCCtx", "ZSTD_createCCtx", "ZSTD_freeCCtx",
         "ZSTD_CCtx_reset", "ZSTD_versionNumber")
providers = {name: {"selected": provider(zstd, name), "global": provider(default, name)} for name in names}
zstd.ZSTD_versionNumber.argtypes = []
zstd.ZSTD_versionNumber.restype = ctypes.c_uint
print(json.dumps({"case": case, "providers": providers, "directVersion": zstd.ZSTD_versionNumber()}), flush=True)
zstd.ZSTD_compressBound.argtypes = [ctypes.c_size_t]
zstd.ZSTD_compressBound.restype = ctypes.c_size_t
zstd.ZSTD_compress.argtypes = [ctypes.c_void_p, ctypes.c_size_t, ctypes.c_void_p, ctypes.c_size_t, ctypes.c_int]
zstd.ZSTD_compress.restype = ctypes.c_size_t
zstd.ZSTD_isError.argtypes = [ctypes.c_size_t]
zstd.ZSTD_isError.restype = ctypes.c_uint
source = ctypes.create_string_buffer(b"OWNED_VALID_COMPRESSION_CONTROL_DATA" * 4096)
capacity = zstd.ZSTD_compressBound(len(source.raw))
destination = ctypes.create_string_buffer(capacity)
size = zstd.ZSTD_compress(destination, capacity, source, len(source.raw), 3)
if zstd.ZSTD_isError(size):
    raise RuntimeError("Valid codec control returned an explicit compression error.")
print(json.dumps({"case": case, "validCompressionSucceeded": True}), flush=True)
