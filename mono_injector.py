#!/usr/bin/env python3
"""Minimal x64 Mono injector used by the Broforce life trainer.

The injector loads one managed assembly into the already-running Broforce Mono
domain. It does not patch files in the Steam directory.
"""

from __future__ import annotations

import ctypes
from ctypes import wintypes
from dataclasses import dataclass
from pathlib import Path
import struct


if ctypes.sizeof(ctypes.c_void_p) != 8:
    raise RuntimeError("Broforce is 64-bit; use a 64-bit Python/trainer build.")


TH32CS_SNAPPROCESS = 0x00000002
TH32CS_SNAPMODULE = 0x00000008
TH32CS_SNAPMODULE32 = 0x00000010
PROCESS_CREATE_THREAD = 0x0002
PROCESS_QUERY_INFORMATION = 0x0400
PROCESS_VM_OPERATION = 0x0008
PROCESS_VM_WRITE = 0x0020
PROCESS_VM_READ = 0x0010
MEM_COMMIT = 0x1000
MEM_RESERVE = 0x2000
MEM_RELEASE = 0x8000
PAGE_EXECUTE_READWRITE = 0x40
WAIT_OBJECT_0 = 0
WAIT_TIMEOUT = 258
INFINITE = 0xFFFFFFFF
DONT_RESOLVE_DLL_REFERENCES = 0x00000001
INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value


class PROCESSENTRY32W(ctypes.Structure):
    _fields_ = [
        ("dwSize", wintypes.DWORD),
        ("cntUsage", wintypes.DWORD),
        ("th32ProcessID", wintypes.DWORD),
        ("th32DefaultHeapID", ctypes.c_size_t),
        ("th32ModuleID", wintypes.DWORD),
        ("cntThreads", wintypes.DWORD),
        ("th32ParentProcessID", wintypes.DWORD),
        ("pcPriClassBase", wintypes.LONG),
        ("dwFlags", wintypes.DWORD),
        ("szExeFile", wintypes.WCHAR * 260),
    ]


class MODULEENTRY32W(ctypes.Structure):
    _fields_ = [
        ("dwSize", wintypes.DWORD),
        ("th32ModuleID", wintypes.DWORD),
        ("th32ProcessID", wintypes.DWORD),
        ("GlblcntUsage", wintypes.DWORD),
        ("ProccntUsage", wintypes.DWORD),
        ("modBaseAddr", ctypes.POINTER(ctypes.c_ubyte)),
        ("modBaseSize", wintypes.DWORD),
        ("hModule", wintypes.HMODULE),
        ("szModule", wintypes.WCHAR * 256),
        ("szExePath", wintypes.WCHAR * 260),
    ]


kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

kernel32.CreateToolhelp32Snapshot.argtypes = [wintypes.DWORD, wintypes.DWORD]
kernel32.CreateToolhelp32Snapshot.restype = wintypes.HANDLE
kernel32.Process32FirstW.argtypes = [wintypes.HANDLE, ctypes.POINTER(PROCESSENTRY32W)]
kernel32.Process32FirstW.restype = wintypes.BOOL
kernel32.Process32NextW.argtypes = [wintypes.HANDLE, ctypes.POINTER(PROCESSENTRY32W)]
kernel32.Process32NextW.restype = wintypes.BOOL
kernel32.Module32FirstW.argtypes = [wintypes.HANDLE, ctypes.POINTER(MODULEENTRY32W)]
kernel32.Module32FirstW.restype = wintypes.BOOL
kernel32.Module32NextW.argtypes = [wintypes.HANDLE, ctypes.POINTER(MODULEENTRY32W)]
kernel32.Module32NextW.restype = wintypes.BOOL
kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
kernel32.OpenProcess.restype = wintypes.HANDLE
kernel32.VirtualAllocEx.argtypes = [
    wintypes.HANDLE,
    ctypes.c_void_p,
    ctypes.c_size_t,
    wintypes.DWORD,
    wintypes.DWORD,
]
kernel32.VirtualAllocEx.restype = ctypes.c_void_p
kernel32.VirtualFreeEx.argtypes = [
    wintypes.HANDLE,
    ctypes.c_void_p,
    ctypes.c_size_t,
    wintypes.DWORD,
]
kernel32.VirtualFreeEx.restype = wintypes.BOOL
kernel32.WriteProcessMemory.argtypes = [
    wintypes.HANDLE,
    ctypes.c_void_p,
    ctypes.c_void_p,
    ctypes.c_size_t,
    ctypes.POINTER(ctypes.c_size_t),
]
kernel32.WriteProcessMemory.restype = wintypes.BOOL
kernel32.CreateRemoteThread.argtypes = [
    wintypes.HANDLE,
    ctypes.c_void_p,
    ctypes.c_size_t,
    ctypes.c_void_p,
    ctypes.c_void_p,
    wintypes.DWORD,
    ctypes.POINTER(wintypes.DWORD),
]
kernel32.CreateRemoteThread.restype = wintypes.HANDLE
kernel32.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
kernel32.WaitForSingleObject.restype = wintypes.DWORD
kernel32.GetExitCodeThread.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
kernel32.GetExitCodeThread.restype = wintypes.BOOL
kernel32.LoadLibraryExW.argtypes = [wintypes.LPCWSTR, wintypes.HANDLE, wintypes.DWORD]
kernel32.LoadLibraryExW.restype = wintypes.HMODULE
kernel32.GetProcAddress.argtypes = [wintypes.HMODULE, wintypes.LPCSTR]
kernel32.GetProcAddress.restype = ctypes.c_void_p
kernel32.FreeLibrary.argtypes = [wintypes.HMODULE]
kernel32.FreeLibrary.restype = wintypes.BOOL
kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
kernel32.CloseHandle.restype = wintypes.BOOL


class InjectorError(RuntimeError):
    pass


@dataclass(frozen=True)
class RemoteModule:
    base: int
    size: int
    path: Path


def _win32_error(message: str) -> InjectorError:
    code = ctypes.get_last_error()
    return InjectorError(f"{message} (Win32 {code}: {ctypes.FormatError(code).strip()})")


def find_process(executable_name: str = "Broforce_beta.exe") -> int:
    snapshot = kernel32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
    if snapshot == INVALID_HANDLE_VALUE:
        raise _win32_error("无法枚举进程")
    try:
        entry = PROCESSENTRY32W()
        entry.dwSize = ctypes.sizeof(entry)
        if not kernel32.Process32FirstW(snapshot, ctypes.byref(entry)):
            raise _win32_error("无法读取进程列表")
        while True:
            if entry.szExeFile.lower() == executable_name.lower():
                return int(entry.th32ProcessID)
            if not kernel32.Process32NextW(snapshot, ctypes.byref(entry)):
                break
    finally:
        kernel32.CloseHandle(snapshot)
    raise InjectorError("未找到 Broforce，请先通过 Steam 启动游戏。")


def find_remote_module(pid: int, module_name: str) -> RemoteModule:
    snapshot = kernel32.CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid)
    if snapshot == INVALID_HANDLE_VALUE:
        raise _win32_error("无法枚举游戏模块")
    try:
        entry = MODULEENTRY32W()
        entry.dwSize = ctypes.sizeof(entry)
        if not kernel32.Module32FirstW(snapshot, ctypes.byref(entry)):
            raise _win32_error("无法读取游戏模块")
        while True:
            if entry.szModule.lower() == module_name.lower():
                return RemoteModule(
                    base=ctypes.cast(entry.modBaseAddr, ctypes.c_void_p).value or 0,
                    size=int(entry.modBaseSize),
                    path=Path(entry.szExePath),
                )
            if not kernel32.Module32NextW(snapshot, ctypes.byref(entry)):
                break
    finally:
        kernel32.CloseHandle(snapshot)
    raise InjectorError("Broforce 的 mono.dll 尚未加载，请稍等几秒后重试。")


def _export_rvas(module_path: Path, names: tuple[str, ...]) -> dict[str, int]:
    local_module = kernel32.LoadLibraryExW(str(module_path), None, DONT_RESOLVE_DLL_REFERENCES)
    if not local_module:
        raise _win32_error(f"无法读取 Mono 导出表：{module_path}")
    try:
        base = int(local_module)
        result: dict[str, int] = {}
        for name in names:
            address = kernel32.GetProcAddress(local_module, name.encode("ascii"))
            if not address:
                raise InjectorError(f"当前 Mono 版本缺少导出函数：{name}")
            result[name] = int(address) - base
        return result
    finally:
        kernel32.FreeLibrary(local_module)


class _CodeBuilder:
    def __init__(self) -> None:
        self.code = bytearray()
        self.labels: dict[str, int] = {}
        self.fixups: list[tuple[int, str]] = []

    def emit(self, data: bytes) -> None:
        self.code.extend(data)

    def imm64(self, value: int) -> None:
        self.code.extend(struct.pack("<Q", value))

    def label(self, name: str) -> None:
        self.labels[name] = len(self.code)

    def jump32(self, opcode: bytes, label: str) -> None:
        self.emit(opcode)
        self.fixups.append((len(self.code), label))
        self.emit(b"\x00\x00\x00\x00")

    def finish(self) -> bytes:
        for offset, label in self.fixups:
            if label not in self.labels:
                raise InjectorError(f"内部代码标签不存在：{label}")
            displacement = self.labels[label] - (offset + 4)
            self.code[offset : offset + 4] = struct.pack("<i", displacement)
        return bytes(self.code)


def _build_stub(functions: dict[str, int], pointers: dict[str, int]) -> bytes:
    code = _CodeBuilder()
    code.emit(b"\x53")  # push rbx
    code.emit(b"\x48\x83\xEC\x20")  # sub rsp, 0x20 (shadow space + aligned stack)

    # MonoDomain* domain = mono_get_root_domain()
    code.emit(b"\x48\xB8")
    code.imm64(functions["mono_get_root_domain"])
    code.emit(b"\xFF\xD0\x48\x89\xC3\x48\x85\xDB")
    code.jump32(b"\x0F\x84", "fail_1")

    # mono_thread_attach(domain)
    code.emit(b"\x48\x89\xD9\x48\xB8")
    code.imm64(functions["mono_thread_attach"])
    code.emit(b"\xFF\xD0")

    # MonoAssembly* assembly = mono_domain_assembly_open(domain, path)
    code.emit(b"\x48\x89\xD9\x48\xBA")
    code.imm64(pointers["path"])
    code.emit(b"\x48\xB8")
    code.imm64(functions["mono_domain_assembly_open"])
    code.emit(b"\xFF\xD0\x48\x85\xC0")
    code.jump32(b"\x0F\x84", "fail_2")

    # MonoImage* image = mono_assembly_get_image(assembly)
    code.emit(b"\x48\x89\xC1\x48\xB8")
    code.imm64(functions["mono_assembly_get_image"])
    code.emit(b"\xFF\xD0\x48\x85\xC0")
    code.jump32(b"\x0F\x84", "fail_3")

    # MonoClass* klass = mono_class_from_name(image, namespace, class)
    code.emit(b"\x48\x89\xC1\x48\xBA")
    code.imm64(pointers["namespace"])
    code.emit(b"\x49\xB8")
    code.imm64(pointers["class"])
    code.emit(b"\x48\xB8")
    code.imm64(functions["mono_class_from_name"])
    code.emit(b"\xFF\xD0\x48\x85\xC0")
    code.jump32(b"\x0F\x84", "fail_4")

    # MonoMethod* method = mono_class_get_method_from_name(klass, method, 0)
    code.emit(b"\x48\x89\xC1\x48\xBA")
    code.imm64(pointers["method"])
    code.emit(b"\x45\x31\xC0\x48\xB8")
    code.imm64(functions["mono_class_get_method_from_name"])
    code.emit(b"\xFF\xD0\x48\x85\xC0")
    code.jump32(b"\x0F\x84", "fail_5")

    # mono_runtime_invoke(method, NULL, NULL, &exception)
    code.emit(b"\x48\x89\xC1\x48\x31\xD2\x4D\x31\xC0\x49\xB9")
    code.imm64(pointers["exception"])
    code.emit(b"\x48\xB8")
    code.imm64(functions["mono_runtime_invoke"])
    code.emit(b"\xFF\xD0\x48\xA1")
    code.imm64(pointers["exception"])
    code.emit(b"\x48\x85\xC0")
    code.jump32(b"\x0F\x85", "fail_6")

    code.emit(b"\x31\xC0")  # success: eax = 0
    code.jump32(b"\xE9", "cleanup")
    for number in range(1, 7):
        code.label(f"fail_{number}")
        code.emit(b"\xB8" + struct.pack("<I", number))
        code.jump32(b"\xE9", "cleanup")
    code.label("cleanup")
    code.emit(b"\x48\x83\xC4\x20\x5B\xC3")
    return code.finish()


def inject_managed_bridge(pid: int, bridge_path: Path, timeout_ms: int = 20_000) -> None:
    bridge_path = bridge_path.resolve()
    if not bridge_path.is_file():
        raise InjectorError(f"缺少桥接组件：{bridge_path}")

    mono = find_remote_module(pid, "mono.dll")
    export_names = (
        "mono_get_root_domain",
        "mono_thread_attach",
        "mono_domain_assembly_open",
        "mono_assembly_get_image",
        "mono_class_from_name",
        "mono_class_get_method_from_name",
        "mono_runtime_invoke",
    )
    functions = {
        name: mono.base + rva for name, rva in _export_rvas(mono.path, export_names).items()
    }

    access = (
        PROCESS_CREATE_THREAD
        | PROCESS_QUERY_INFORMATION
        | PROCESS_VM_OPERATION
        | PROCESS_VM_WRITE
        | PROCESS_VM_READ
    )
    process = kernel32.OpenProcess(access, False, pid)
    if not process:
        raise _win32_error("无法打开 Broforce 进程；请尝试以管理员身份运行修改器")

    remote = 0
    thread = None
    try:
        allocation_size = 4096
        remote = int(
            kernel32.VirtualAllocEx(
                process,
                None,
                allocation_size,
                MEM_COMMIT | MEM_RESERVE,
                PAGE_EXECUTE_READWRITE,
            )
            or 0
        )
        if not remote:
            raise _win32_error("无法在游戏进程中分配桥接内存")

        strings = {
            "path": str(bridge_path).encode("utf-8") + b"\x00",
            "namespace": b"BroforceLifeTrainer\x00",
            "class": b"Bootstrap\x00",
            "method": b"Start\x00",
        }
        cursor = 1024
        pointers: dict[str, int] = {}
        blob = bytearray(allocation_size)
        for name, data in strings.items():
            pointers[name] = remote + cursor
            blob[cursor : cursor + len(data)] = data
            cursor += len(data)
        cursor = (cursor + 7) & ~7
        pointers["exception"] = remote + cursor

        stub = _build_stub(functions, pointers)
        if len(stub) >= 1024:
            raise InjectorError("内部桥接代码过大")
        blob[: len(stub)] = stub

        written = ctypes.c_size_t()
        source = (ctypes.c_ubyte * len(blob)).from_buffer(blob)
        if not kernel32.WriteProcessMemory(
            process,
            ctypes.c_void_p(remote),
            source,
            len(blob),
            ctypes.byref(written),
        ) or written.value != len(blob):
            raise _win32_error("无法写入桥接代码")

        thread_id = wintypes.DWORD()
        thread = kernel32.CreateRemoteThread(
            process,
            None,
            0,
            ctypes.c_void_p(remote),
            None,
            0,
            ctypes.byref(thread_id),
        )
        if not thread:
            raise _win32_error("无法启动 Mono 桥接线程")

        wait_result = kernel32.WaitForSingleObject(thread, timeout_ms)
        if wait_result == WAIT_TIMEOUT:
            raise InjectorError("桥接加载超时；游戏可能仍在启动，请稍后重试。")
        if wait_result != WAIT_OBJECT_0:
            raise _win32_error("等待桥接线程失败")

        exit_code = wintypes.DWORD()
        if not kernel32.GetExitCodeThread(thread, ctypes.byref(exit_code)):
            raise _win32_error("无法读取桥接结果")
        if exit_code.value != 0:
            stages = {
                1: "获取 Mono 域",
                2: "加载桥接程序集",
                3: "读取桥接映像",
                4: "查找 Bootstrap 类",
                5: "查找 Start 方法",
                6: "执行 Start 方法",
            }
            stage = stages.get(exit_code.value, "未知阶段")
            raise InjectorError(f"Mono 桥接失败：{stage}（代码 {exit_code.value}）")
    finally:
        if thread:
            kernel32.CloseHandle(thread)
        if remote:
            kernel32.VirtualFreeEx(process, ctypes.c_void_p(remote), 0, MEM_RELEASE)
        kernel32.CloseHandle(process)

