#!/usr/bin/env python3
"""Chinese GUI for the Steam Broforce lives and hero-lock trainer."""

from __future__ import annotations

import ctypes
from ctypes import wintypes
import os
from pathlib import Path
import sys
import threading
import time
import tkinter as tk
from tkinter import messagebox, ttk

import mono_injector


PROCESS_NAME = "Broforce_beta.exe"
SUPPORTED_BUILD = "12964083"
INSTALL_MONITOR_MESSAGE = 0x8452
VK_F6 = 0x75
VK_F7 = 0x76
VK_F8 = 0x77
VK_F9 = 0x78
SPAWN_COMPANION_SEQUENCE_OFFSET = 80
SPAWN_VILLAGER_SEQUENCE_OFFSET = 56
WAIT_OBJECT_0 = 0
EVENT_MODIFY_STATE = 0x0002
SYNCHRONIZE = 0x00100000
SMTO_ABORTIFHUNG = 0x0002
FILE_MAP_WRITE = 0x0002
FILE_MAP_READ = 0x0004
SHARED_MEMORY_SIZE = 352
PLAYER_STRIDE = 88
HERO_SWITCH_COMMAND_OFFSET = 64
VILLAGER_AI_DISABLED_OFFSET = 72
LIVES_SNAPSHOT_OFFSET = 0
ADD_LIFE_SEQUENCE_OFFSET = 8
MONITOR_READY_OFFSET = 16
MONITOR_HEARTBEAT_OFFSET = 24
IRONBRO_ADD_LIFE_SEQUENCE_OFFSET = 32
HERO_LOCK_TARGET_OFFSET = 40
UNLOCKED_HERO_MASK_OFFSET = 48
MONITOR_INTERVAL_MS = 250
MONITOR_STALL_NOTICE_SECONDS = 4.0
MONITOR_RECOVERY_SECONDS = 15.0
INSTALL_MONITOR_TIMEOUT_MS = 10_000
ULONG_PTR_MAX = (1 << (ctypes.sizeof(ctypes.c_size_t) * 8)) - 1
STATUS_PATH = (
    Path(os.environ.get("LOCALAPPDATA", Path.home()))
    / "BroforceLifeTrainer"
    / "bridge_status_v12.txt"
)

HERO_NAMES = (
    "Rambro",
    "Brommando",
    "BaBroracus",
    "BrodellWalker",
    "Blade",
    "McBrover",
    "Brononymous",
    "Brobocop",
    "BroDredd",
    "BroHard",
    "MadMaxBrotansky",
    "SnakeBroSkin",
    "Brominator",
    "IndianaBrones",
    "AshBrolliams",
    "Nebro",
    "BoondockBros",
    "Brochete",
    "BronanTheBrobarian",
    "EllenRipbro",
    "CherryBroling",
    "TimeBroVanDamme",
    "ColJamesBroddock",
    "BroniversalSoldier",
    "BroneyRoss",
    "LeeBroxmas",
    "BronnarJensen",
    "HaleTheBro",
    "TrentBroser",
    "Broc",
    "TollBroad",
    "TheBrode",
    "BroMax",
    "DoubleBroSeven",
    "Predabro",
    "TheBrofessional",
    "BrondleFly",
    "BroveHeart",
    "TheBrocketeer",
    "TankBro",
    "Broden",
    "TheBrolander",
    "BroLee",
    "DirtyHarry",
    "CaseyBroback",
    "Xebro",
    "ScorpionBro",
    "Broffy",
    "Desperabro",
    "BroGummer",
    "ChevBrolios",
    "DemolitionBro",
)


# 中文辨认名采用角色原型或音译，并非官方译名；顺序对应游戏人物编号。
HERO_NAMES_ZH = (
    "兰博", "突击队员", "天龙特攻队巴拉克斯", "德州游侠沃克",
    "刀锋战士", "百战天龙", "无名英雄", "机械战警",
    "特警判官", "虎胆龙威", "疯狂麦克斯", "纽约大逃亡斯内克",
    "终结者", "夺宝奇兵琼斯", "鬼玩人艾什", "黑客帝国尼奥",
    "处刑人兄弟", "弯刀", "野蛮人柯南", "异形雷普莉",
    "恐怖星球切丽", "时空特警", "越战先锋布拉多克", "再造战士",
    "敢死队巴尼", "敢死队圣诞", "敢死队贡纳", "敢死队凯撒",
    "敢死队战壕", "敢死队多克", "敢死队托尔", "杀死比尔新娘",
    "末日战士麦克斯", "007 邦德", "铁血战士", "这个杀手不太冷里昂",
    "变蝇人", "勇敢的心华莱士", "火箭人", "坦克女郎",
    "雷电", "高地人", "李小龙", "肮脏的哈里",
    "潜龙轰天凯西", "战士公主西娜", "蝎子", "吸血鬼猎人巴菲",
    "杀人三步曲枪手", "异形魔怪伯特", "怒火攻心切夫", "超级战警",
)


def hero_choice_label(hero_type: int) -> str:
    return f"{hero_type:02d} · {HERO_NAMES_ZH[hero_type]}（{HERO_NAMES[hero_type]}）"


user32 = ctypes.WinDLL("user32", use_last_error=True)
kernel32 = mono_injector.kernel32

user32.EnumWindows.argtypes = [ctypes.c_void_p, wintypes.LPARAM]
user32.EnumWindows.restype = wintypes.BOOL
user32.IsWindowVisible.argtypes = [wintypes.HWND]
user32.IsWindowVisible.restype = wintypes.BOOL
user32.GetWindow.argtypes = [wintypes.HWND, wintypes.UINT]
user32.GetWindow.restype = wintypes.HWND
user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
user32.GetWindowThreadProcessId.restype = wintypes.DWORD
user32.GetForegroundWindow.restype = wintypes.HWND
user32.SendMessageTimeoutW.argtypes = [
    wintypes.HWND,
    wintypes.UINT,
    wintypes.WPARAM,
    wintypes.LPARAM,
    wintypes.UINT,
    wintypes.UINT,
    ctypes.POINTER(ctypes.c_size_t),
]
user32.SendMessageTimeoutW.restype = wintypes.LPARAM
user32.GetAsyncKeyState.argtypes = [ctypes.c_int]
user32.GetAsyncKeyState.restype = wintypes.SHORT
kernel32.CreateEventW.argtypes = [ctypes.c_void_p, wintypes.BOOL, wintypes.BOOL, wintypes.LPCWSTR]
kernel32.CreateEventW.restype = wintypes.HANDLE
kernel32.ResetEvent.argtypes = [wintypes.HANDLE]
kernel32.ResetEvent.restype = wintypes.BOOL
kernel32.OpenFileMappingW.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.LPCWSTR]
kernel32.OpenFileMappingW.restype = wintypes.HANDLE
kernel32.MapViewOfFile.argtypes = [
    wintypes.HANDLE,
    wintypes.DWORD,
    wintypes.DWORD,
    wintypes.DWORD,
    ctypes.c_size_t,
]
kernel32.MapViewOfFile.restype = ctypes.c_void_p
kernel32.UnmapViewOfFile.argtypes = [ctypes.c_void_p]
kernel32.UnmapViewOfFile.restype = wintypes.BOOL


def bundled_bridge_path() -> Path:
    base = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parent))
    candidates = (
        base / "BroforceLifeBridge.dll",
        base / "managed" / "BroforceLifeBridge.dll",
        Path(__file__).resolve().parent / "managed" / "BroforceLifeBridge.dll",
    )
    for candidate in candidates:
        if candidate.is_file():
            return candidate
    return candidates[0]


def hero_portrait_path(hero_type: int) -> Path:
    if not 0 <= hero_type < len(HERO_NAMES):
        raise ValueError(f"invalid hero type: {hero_type}")
    base = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parent))
    return base / "assets" / "hero_portraits" / f"{hero_type:02d}_{HERO_NAMES[hero_type]}.png"


def find_game_window(pid: int) -> int:
    result = 0
    callback_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

    def visit(h_window: int, unused: int) -> bool:
        nonlocal result
        process_id = wintypes.DWORD()
        user32.GetWindowThreadProcessId(h_window, ctypes.byref(process_id))
        if (
            process_id.value == pid
            and user32.IsWindowVisible(h_window)
            and not user32.GetWindow(h_window, 4)
        ):
            result = int(h_window)
            return False
        return True

    callback = callback_type(visit)
    user32.EnumWindows(callback, 0)
    return result


def create_ready_event(pid: int) -> int:
    handle = kernel32.CreateEventW(
        None,
        True,
        False,
        f"Local\\BroforceLifeTrainer_ReadyV13_{pid}",
    )
    if not handle:
        code = ctypes.get_last_error()
        raise mono_injector.InjectorError(
            f"无法创建连接事件（Win32 {code}: {ctypes.FormatError(code).strip()}）"
        )
    if not kernel32.ResetEvent(handle):
        code = ctypes.get_last_error()
        kernel32.CloseHandle(handle)
        raise mono_injector.InjectorError(
            f"无法重置连接事件（Win32 {code}: {ctypes.FormatError(code).strip()}）"
        )
    return int(handle)


def parse_bridge_status(text: str) -> tuple[str, int | None, list[str]]:
    parts = text.strip().split("|")
    if len(parts) < 2:
        return "INVALID", None, []
    try:
        pid = int(parts[1])
    except ValueError:
        pid = None
    return parts[0], pid, parts[2:]


def decode_lives_result(raw_value: int) -> tuple[str, int | None, int | None]:
    """Decode the bridge's compact, read-only lives snapshot."""
    if raw_value == ULONG_PTR_MAX:
        return "ONLINE", None, None
    if raw_value == 0:
        return "WAITING", None, None

    player_tag = (raw_value >> 32) & 0xFFFFFFFF
    lives = raw_value & 0xFFFFFFFF
    if not 1 <= player_tag <= 4:
        return "INVALID", None, None
    return "OK", player_tag - 1, lives


def open_shared_state(pid: int) -> tuple[int, int]:
    handle = kernel32.OpenFileMappingW(
        FILE_MAP_READ | FILE_MAP_WRITE,
        False,
        f"Local\\BroforceLifeTrainer_SharedV13_{pid}",
    )
    if not handle:
        code = ctypes.get_last_error()
        raise mono_injector.InjectorError(
            f"无法打开实时状态（Win32 {code}: {ctypes.FormatError(code).strip()}）"
        )

    view = kernel32.MapViewOfFile(
        handle,
        FILE_MAP_READ | FILE_MAP_WRITE,
        0,
        0,
        SHARED_MEMORY_SIZE,
    )
    if not view:
        code = ctypes.get_last_error()
        kernel32.CloseHandle(handle)
        raise mono_injector.InjectorError(
            f"无法映射实时状态（Win32 {code}: {ctypes.FormatError(code).strip()}）"
        )
    return int(handle), int(view)


def close_shared_state(handle: int, view: int) -> None:
    if view:
        kernel32.UnmapViewOfFile(view)
    if handle:
        kernel32.CloseHandle(handle)


def read_shared_uint64(view: int, offset: int) -> int:
    return ctypes.c_uint64.from_address(view + offset).value


def read_shared_int64(view: int, offset: int) -> int:
    return ctypes.c_int64.from_address(view + offset).value


def monitor_is_ready(view: int) -> bool:
    return read_shared_uint64(view, MONITOR_READY_OFFSET) == 1


def read_monitor_heartbeat(view: int) -> int:
    return read_shared_uint64(view, MONITOR_HEARTBEAT_OFFSET)


def classify_monitor_health(is_ready: bool, heartbeat_age_seconds: float) -> str:
    """Classify monitor health without mistaking an ordinary level load for failure."""
    if not is_ready or heartbeat_age_seconds >= MONITOR_RECOVERY_SECONDS:
        return "RECOVER"
    if heartbeat_age_seconds >= MONITOR_STALL_NOTICE_SECONDS:
        return "BUSY"
    return "HEALTHY"


def install_lives_monitor(game_window: int) -> bool:
    result = ctypes.c_size_t()
    succeeded = user32.SendMessageTimeoutW(
        game_window,
        INSTALL_MONITOR_MESSAGE,
        0,
        0,
        SMTO_ABORTIFHUNG,
        INSTALL_MONITOR_TIMEOUT_MS,
        ctypes.byref(result),
    )
    return bool(succeeded and result.value == 1)


def player_view(shared_view: int, player_index: int) -> int:
    if not 0 <= player_index < 4:
        raise ValueError("本地玩家编号必须为 0–3")
    return shared_view + player_index * PLAYER_STRIDE


def query_player_lives(shared_view: int, player_index: int = 0) -> tuple[str, int | None, int | None]:
    shared_view = player_view(shared_view, player_index)
    return decode_lives_result(read_shared_uint64(shared_view, LIVES_SNAPSHOT_OFFSET))


def request_add_life(shared_view: int, player_index: int = 0) -> None:
    shared_view = player_view(shared_view, player_index)
    address = shared_view + ADD_LIFE_SEQUENCE_OFFSET
    sequence = ctypes.c_uint64.from_address(address).value
    ctypes.c_uint64.from_address(address).value = (sequence + 1) & ULONG_PTR_MAX


def request_ironbro_add_life(shared_view: int, player_index: int = 0) -> None:
    shared_view = player_view(shared_view, player_index)
    address = shared_view + IRONBRO_ADD_LIFE_SEQUENCE_OFFSET
    sequence = ctypes.c_uint64.from_address(address).value
    ctypes.c_uint64.from_address(address).value = (sequence + 1) & ULONG_PTR_MAX


def request_spawn_villager(shared_view: int, player_index: int = 0) -> None:
    shared_view = player_view(shared_view, player_index)
    address = shared_view + SPAWN_VILLAGER_SEQUENCE_OFFSET
    value = ctypes.c_uint64.from_address(address).value
    ctypes.c_uint64.from_address(address).value = (value + 1) & ULONG_PTR_MAX


def request_spawn_companion(shared_view: int, hero_type: int, player_index: int = 0) -> None:
    if not 0 <= hero_type < len(HERO_NAMES):
        raise ValueError("Invalid companion hero")
    address = player_view(shared_view, player_index) + SPAWN_COMPANION_SEQUENCE_OFFSET
    previous = ctypes.c_uint64.from_address(address).value
    sequence = ((previous >> 8) + 1) & ((1 << 56) - 1)
    ctypes.c_uint64.from_address(address).value = (sequence << 8) | hero_type


def set_villager_ai(shared_view: int, enabled: bool, player_index: int = 0) -> None:
    address = player_view(shared_view, player_index) + VILLAGER_AI_DISABLED_OFFSET
    ctypes.c_uint64.from_address(address).value = int(not enabled)


def request_hero_switch(shared_view: int, hero_type: int, player_index: int = 0) -> None:
    shared_view = player_view(shared_view, player_index)
    # Publish sequence and target in one aligned 64-bit write.
    address = shared_view + HERO_SWITCH_COMMAND_OFFSET
    previous = ctypes.c_uint64.from_address(address).value
    sequence = ((previous >> 8) + 1) & ((1 << 56) - 1)
    ctypes.c_uint64.from_address(address).value = (sequence << 8) | hero_type


def read_hero_lock_target(shared_view: int, player_index: int = 0) -> int:
    shared_view = player_view(shared_view, player_index)
    return read_shared_int64(shared_view, HERO_LOCK_TARGET_OFFSET)


def request_hero_lock(shared_view: int, hero_type: int, player_index: int = 0) -> None:
    shared_view = player_view(shared_view, player_index)
    ctypes.c_int64.from_address(shared_view + HERO_LOCK_TARGET_OFFSET).value = hero_type


def read_unlocked_hero_mask(shared_view: int) -> int:
    return read_shared_uint64(shared_view, UNLOCKED_HERO_MASK_OFFSET)


def hero_options_from_mask(mask: int) -> list[tuple[int, str]]:
    return [
        (hero_type, name)
        for hero_type, name in enumerate(HERO_NAMES)
        if mask & (1 << hero_type)
    ]


class TrainerApp:
    def __init__(self, root: tk.Tk) -> None:
        self.root = root
        self.root.title("Broforce 命数 / 人物修改器")
        self.root.geometry("620x530")
        self.root.minsize(620, 460)

        self.pid: int | None = None
        self.game_window = 0
        self.ready_event = 0
        self.shared_handle = 0
        self.shared_view = 0
        self.connecting = False
        self.was_f6_down = False
        self.was_f7_down = False
        self.was_f8_down = False
        self.was_f9_down = False
        self.last_status_mtime = 0
        self.last_monitor_heartbeat = 0
        self.last_monitor_heartbeat_time = time.monotonic()
        self.last_unlocked_hero_mask: int | None = None
        self.last_hero_lock_target: int | None = None
        self.hero_display_to_id: dict[str, int] = {}
        self.hero_portrait_image: tk.PhotoImage | None = None
        self.hero_gallery: tk.Toplevel | None = None
        self.hero_cards: dict[int, tk.Button] = {}
        self.hero_images: dict[int, tk.PhotoImage] = {}
        self.hero_controls_enabled = False

        self.connection_var = tk.StringVar(value="等待 Broforce 启动")
        self.result_var = tk.StringVar(value="进入单机关卡后：F6 普通加命｜F7 钢铁兄弟加命")
        self.lives_var = tk.StringVar(value="—")
        self.monitor_var = tk.StringVar(value="实时监视：未连接")
        self.player_choice_var = tk.StringVar(value="玩家 1")
        self.villager_ai_var = tk.BooleanVar(value=True)
        self.hero_choice_var = tk.StringVar(value="等待连接并读取已解锁人物")
        self.hero_lock_var = tk.StringVar(value="人物锁定：未连接")

        self._build_ui()
        self.root.protocol("WM_DELETE_WINDOW", self._close)
        self.root.after(250, self._connection_tick)
        self.root.after(30, self._hotkey_tick)
        self.root.after(300, self._status_tick)
        self.root.after(MONITOR_INTERVAL_MS, self._lives_tick)

    def _build_ui(self) -> None:
        style = ttk.Style()
        if "vista" in style.theme_names():
            style.theme_use("vista")
        outer = ttk.Frame(self.root, padding=12)
        outer.pack(fill="both", expand=True)

        header = ttk.Frame(outer)
        header.pack(fill="x", pady=(0, 8))
        ttk.Label(header, text="Broforce 修改器",
                  font=("Microsoft YaHei UI", 14, "bold")).pack(side="left")
        ttk.Button(header, text="使用说明", command=self._show_help).pack(side="right")

        row = ttk.Frame(outer)
        row.pack(fill="x")
        self.connect_button = ttk.Button(row, text="连接 / 重连", command=self.start_connect)
        self.connect_button.pack(side="left")
        ttk.Label(row, textvariable=self.connection_var).pack(side="left", padx=10)
        self.progress = ttk.Progressbar(outer, mode="indeterminate", length=120)
        self.progress.pack(fill="x", pady=(5, 8))

        player_row = ttk.Frame(outer)
        player_row.pack(fill="x", pady=(0, 6))
        ttk.Label(player_row, text="操作玩家：").pack(side="left")
        self.player_combo = ttk.Combobox(player_row, textvariable=self.player_choice_var,
            values=[f"玩家 {i}" for i in range(1, 5)], state="readonly", width=8)
        self.player_combo.pack(side="left")
        self.player_combo.bind("<<ComboboxSelected>>", self._on_player_selected)
        ttk.Label(player_row, text="命数：").pack(side="left", padx=(22, 0))
        ttk.Label(player_row, textvariable=self.lives_var,
                  font=("Microsoft YaHei UI", 18, "bold")).pack(side="left")

        actions = ttk.Frame(outer)
        actions.pack(fill="x", pady=(0, 9))
        self.add_button = ttk.Button(actions, text="普通 +1 命 [F6]",
            command=self.add_life, state="disabled")
        self.ironbro_add_button = ttk.Button(actions, text="钢铁兄弟 +1 命 [F7]",
            command=self.add_ironbro_life, state="disabled")
        self.spawn_villager_button = ttk.Button(actions, text="武装平民 [F8]",
            command=self.spawn_villager, state="disabled")
        for i, button in enumerate((self.add_button, self.ironbro_add_button, self.spawn_villager_button)):
            actions.columnconfigure(i, weight=1, uniform="actions")
            button.grid(row=0, column=i, sticky="ew", padx=(0 if i == 0 else 6, 0), ipady=2)

        self.spawn_companion_button = ttk.Button(outer, text="生成所选人物为电脑队友 [F9]",
            command=self.spawn_companion, state="disabled")
        self.spawn_companion_button.pack(fill="x", pady=(0, 7))

        ttk.Checkbutton(outer, text="村民强袭 AI（当前玩家：主动进攻 / 快速射击）",
                        variable=self.villager_ai_var, command=self._toggle_villager_ai).pack(
                            anchor="w", pady=(0, 7))
        hero_lock = ttk.LabelFrame(outer, text="人物（F9 使用这里选中的角色）", padding=8)
        hero_lock.pack(fill="x", pady=(0, 8))
        portrait_frame = ttk.Frame(hero_lock, width=76, height=108)
        portrait_frame.pack(side="left", padx=(0, 10))
        portrait_frame.pack_propagate(False)
        self.hero_portrait_label = tk.Label(portrait_frame, text="等待\n人物",
            foreground="#aaaaaa", background="#1b1b1b", relief="sunken",
            borderwidth=1, font=("Microsoft YaHei UI", 9))
        self.hero_portrait_label.pack(fill="both", expand=True)
        hero_controls = ttk.Frame(hero_lock)
        hero_controls.pack(side="left", fill="both", expand=True)
        self.hero_combo = ttk.Combobox(hero_controls, textvariable=self.hero_choice_var,
            state="disabled", width=30)
        self.hero_combo.pack(fill="x")
        self.hero_combo.bind("<<ComboboxSelected>>", self._on_hero_selected)
        hero_buttons = ttk.Frame(hero_controls)
        hero_buttons.pack(fill="x", pady=(7, 0))
        self.hero_switch_button = ttk.Button(hero_buttons, text="仅切换一次",
            command=self.switch_hero, state="disabled")
        self.hero_lock_button = ttk.Button(hero_buttons, text="启用 / 更换锁定",
            command=self.enable_hero_lock, state="disabled")
        self.hero_unlock_button = ttk.Button(hero_buttons, text="关闭锁定",
            command=self.disable_hero_lock, state="disabled")
        self.hero_gallery_button = ttk.Button(hero_buttons, text="头像选人…",
            command=self._open_hero_gallery, state="disabled")
        for i, button in enumerate((self.hero_switch_button, self.hero_lock_button,
                                   self.hero_unlock_button, self.hero_gallery_button)):
            button.pack(side="left", padx=(0 if i == 0 else 5, 0))
        ttk.Label(hero_controls, textvariable=self.hero_lock_var, foreground="#555555",
                  wraplength=465).pack(anchor="w", pady=(7, 0))

        ttk.Label(outer, textvariable=self.monitor_var, foreground="#777777",
                  wraplength=590).pack(anchor="w")
        self.result_label = ttk.Label(outer, textvariable=self.result_var,
            foreground="#444444", wraplength=590, justify="left")
        self.result_label.pack(anchor="w", pady=(5, 0))

    def _show_help(self) -> None:
        messagebox.showinfo("使用说明", (
            "选择玩家 1–4 后，按钮和 F6 / F7 / F8 / F9 均操作该玩家。\n"
            "快捷键仅在游戏前台生效，每名玩家可独立锁定人物。\n\n"
            "仅切换一次：成功后关闭该玩家原有锁定。\n"
            "启用 / 更换锁定：换人并在复活、救人和切关后保持。\n"
            "人物切换和锁定仅支持普通本地模式的已解锁人物。\n\n"
            "钢铁兄弟加命：优先修复幽灵命数或复活死亡角色，\n"
            "否则增加可用角色；命数和角色名单由整局共享。\n"
            "武装平民：在所选玩家身旁生成，全队本关上限 20 名。\n"
            "强袭 AI：主动寻敌并接近，持续快速射击；距离过远时返回玩家，卡住重新寻路。\n"
            "开关按玩家独立设置，影响该玩家已生成及后续生成的村民；关闭恢复原生 AI。\n"
            "电脑队友 F9：使用下方人物列表或头像选人，生成所选已解锁人物，自动跟随所选玩家并追踪攻击敌人；全队本关上限 4 名。\n"
            "仅普通本地合作关卡；死亡后消失，切关后需重新生成；不占玩家席位。\n"
            "首次使用 V13 请重启游戏及修改器。\n\n"
            f"仅单机 / 本地游戏；适用 Steam 构建 {SUPPORTED_BUILD}。\n"
            "修改器不写入游戏安装目录，重启游戏后需重新连接。"
        ), parent=self.root)

    def _on_hero_selected(self, unused_event: object | None = None) -> None:
        self._refresh_hero_portrait()
        self._update_hero_gallery()

    def _open_hero_gallery(self) -> None:
        if not self.hero_controls_enabled:
            return
        if self.hero_gallery is not None and self.hero_gallery.winfo_exists():
            self.hero_gallery.lift()
            return
        self.hero_gallery = gallery = tk.Toplevel(self.root)
        gallery.title("按头像选择人物")
        gallery.geometry("680x520")
        gallery.minsize(680, 380)
        gallery.transient(self.root)
        ttk.Label(gallery, text="选中头像，再选择单次切换或锁定",
                  font=("Microsoft YaHei UI", 12, "bold")).pack(anchor="w", padx=14, pady=10)
        footer = ttk.Frame(gallery, padding=12)
        footer.pack(side="bottom", fill="x")
        ttk.Label(footer, textvariable=self.hero_choice_var).pack(anchor="w")
        ttk.Label(footer, textvariable=self.hero_lock_var).pack(anchor="w", pady=4)
        self.gallery_switch_button = ttk.Button(
            footer, text="仅切换一次", command=self.switch_hero)
        self.gallery_switch_button.pack(side="left", padx=(0, 8))
        self.gallery_lock_button = ttk.Button(
            footer, text="启用 / 更换锁定", command=self.enable_hero_lock)
        self.gallery_lock_button.pack(side="left")
        ttk.Button(footer, text="返回", command=gallery.destroy).pack(side="right")
        area = ttk.Frame(gallery, padding=(12, 0))
        area.pack(fill="both", expand=True)
        canvas = tk.Canvas(area, highlightthickness=0)
        scrollbar = ttk.Scrollbar(area, orient="vertical", command=canvas.yview)
        scrollbar.pack(side="right", fill="y")
        canvas.pack(side="left", fill="both", expand=True)
        canvas.configure(yscrollcommand=scrollbar.set)
        self.gallery_grid = ttk.Frame(canvas)
        window = canvas.create_window((0, 0), window=self.gallery_grid, anchor="nw")
        canvas.bind("<Configure>", lambda event: canvas.itemconfigure(window, width=event.width))
        self.gallery_grid.bind("<Configure>", lambda event: canvas.configure(scrollregion=canvas.bbox("all")))
        # Toplevel binding also receives mouse-wheel events from its child cards.
        gallery.bind("<MouseWheel>", lambda event: canvas.yview_scroll(
            -1 if event.delta > 0 else 1, "units"))
        self.hero_cards = {}
        self._update_hero_gallery()

    def _select_hero_card(self, hero_type: int) -> None:
        if not self.hero_controls_enabled or hero_type not in self.hero_display_to_id.values():
            return
        self.hero_choice_var.set(hero_choice_label(hero_type))
        self._on_hero_selected()

    def _update_hero_gallery(self) -> None:
        if self.hero_gallery is None or not self.hero_gallery.winfo_exists():
            return
        ids = list(self.hero_display_to_id.values())
        if list(self.hero_cards) != ids:
            for widget in self.gallery_grid.winfo_children():
                widget.destroy()
            self.hero_cards = {}
            for index, hero_type in enumerate(ids):
                try:
                    if hero_type not in self.hero_images:
                        self.hero_images[hero_type] = tk.PhotoImage(
                            file=str(hero_portrait_path(hero_type))).zoom(2, 2)
                    portrait = self.hero_images[hero_type]
                except (OSError, tk.TclError):
                    portrait = ""
                card = tk.Button(
                    self.gallery_grid, image=portrait, compound="top",
                    text=HERO_NAMES_ZH[hero_type], wraplength=108,
                    width=112 if portrait else 14, height=132 if portrait else 7,
                    font=("Microsoft YaHei UI", 9), cursor="hand2",
                    command=lambda hero=hero_type: self._select_hero_card(hero),
                )
                card.grid(row=index // 5, column=index % 5, padx=3, pady=4, sticky="nsew")
                self.hero_cards[hero_type] = card
            if not ids:
                ttk.Label(self.gallery_grid, text="等待连接游戏并读取已解锁人物").grid(padx=15, pady=25)
        selected = self.hero_display_to_id.get(self.hero_choice_var.get())
        target = self.last_hero_lock_target
        for hero_type, card in self.hero_cards.items():
            chosen = hero_type == selected
            locked = hero_type == target
            card.configure(
                text=("✓ 已选 · " if chosen else "") + HERO_NAMES_ZH[hero_type]
                     + ("\n[已锁定]" if locked else ""),
                background="#cce8ff" if chosen else "#e1f3df" if locked else "#f0f0f0",
                relief="sunken" if chosen else "raised",
                state="normal" if self.hero_controls_enabled else "disabled",
            )
        self.gallery_switch_button.configure(
            state="normal" if self.hero_controls_enabled and ids else "disabled")
        self.gallery_lock_button.configure(
            state="normal" if self.hero_controls_enabled and ids else "disabled")

    def _refresh_hero_portrait(self) -> None:
        hero_type = self.hero_display_to_id.get(self.hero_choice_var.get())
        if hero_type is None:
            self.hero_portrait_image = None
            self.hero_portrait_label.configure(image="", text="等待\n人物")
            return

        try:
            source = tk.PhotoImage(file=str(hero_portrait_path(hero_type)))
            self.hero_portrait_image = source.zoom(2, 2)
        except (OSError, tk.TclError):
            self.hero_portrait_image = None
            self.hero_portrait_label.configure(image="", text="暂无\n头像")
            return
        self.hero_portrait_label.configure(image=self.hero_portrait_image, text="")

    def _close_ready_event(self) -> None:
        if self.ready_event:
            kernel32.CloseHandle(self.ready_event)
            self.ready_event = 0

    def _close_shared_state(self) -> None:
        close_shared_state(self.shared_handle, self.shared_view)
        self.shared_handle = 0
        self.shared_view = 0

    def _clear_connection(self, status: str = "等待 Broforce 启动") -> None:
        self._close_ready_event()
        self._close_shared_state()
        self.pid = None
        self.game_window = 0
        self.connection_var.set(status)
        self.progress.stop()
        self.progress.configure(mode="determinate", value=0)
        self.add_button.configure(state="disabled")
        self.ironbro_add_button.configure(state="disabled")
        self.spawn_villager_button.configure(state="disabled")
        self.spawn_companion_button.configure(state="disabled")
        self.hero_combo.configure(state="disabled")
        self.hero_switch_button.configure(state="disabled")
        self.hero_lock_button.configure(state="disabled")
        self.hero_unlock_button.configure(state="disabled")
        self.lives_var.set("—")
        self.monitor_var.set("实时监视：未连接")
        self.hero_lock_var.set("人物锁定：未连接")
        self.hero_choice_var.set("等待连接并读取已解锁人物")
        self.hero_display_to_id = {}
        self.last_unlocked_hero_mask = None
        self.last_hero_lock_target = None
        self.last_monitor_heartbeat = 0
        self.last_monitor_heartbeat_time = time.monotonic()
        self._refresh_hero_portrait()

        self._set_hero_controls_enabled(False)

    def _set_hero_controls_enabled(self, enabled: bool) -> None:
        has_options = bool(self.hero_display_to_id)
        self.hero_controls_enabled = enabled and has_options
        self.hero_gallery_button.configure(state="normal" if self.hero_controls_enabled else "disabled")
        self.hero_combo.configure(state="readonly" if enabled and has_options else "disabled")
        self.hero_switch_button.configure(state="normal" if enabled and has_options else "disabled")
        self.hero_lock_button.configure(state="normal" if enabled and has_options else "disabled")
        target = read_hero_lock_target(self.shared_view, self.selected_player) if self.shared_view else -1
        self.hero_unlock_button.configure(
            state="normal" if enabled and target >= 0 else "disabled"
        )
        self._update_hero_gallery()

    def _refresh_hero_controls(self, enabled: bool = True) -> None:
        if not self.shared_view:
            self._set_hero_controls_enabled(False)
            return

        mask = read_unlocked_hero_mask(self.shared_view)
        target = read_hero_lock_target(self.shared_view, self.selected_player)
        target_changed = target != self.last_hero_lock_target
        if mask != self.last_unlocked_hero_mask:
            selected_id = self.hero_display_to_id.get(self.hero_choice_var.get())
            options = hero_options_from_mask(mask)
            values = [hero_choice_label(hero_type) for hero_type, name in options]
            self.hero_display_to_id = {
                display: hero_type for display, (hero_type, unused) in zip(values, options)
            }
            self.hero_combo.configure(values=values)
            if 0 <= target < len(HERO_NAMES) and mask & (1 << target):
                selected_id = target
            available_ids = {hero_type for hero_type, unused in options}
            if selected_id not in available_ids:
                selected_id = options[0][0] if options else None
            if selected_id is None:
                self.hero_choice_var.set("等待进入游戏并读取已解锁人物")
            else:
                self.hero_choice_var.set(hero_choice_label(selected_id))
            self.last_unlocked_hero_mask = mask

        if (
            target_changed
            and 0 <= target < len(HERO_NAMES)
            and mask & (1 << target)
        ):
            self.hero_choice_var.set(hero_choice_label(target))
        self.last_hero_lock_target = target

        if 0 <= target < len(HERO_NAMES):
            self.hero_lock_var.set(f"人物锁定：已开启｜{HERO_NAMES_ZH[target]}")
        else:
            self.hero_lock_var.set("人物锁定：未开启")
        self._refresh_hero_portrait()
        self._set_hero_controls_enabled(enabled)

    @property
    def selected_player(self) -> int:
        return int(self.player_choice_var.get().split()[-1]) - 1

    def _on_player_selected(self, event=None) -> None:
        self.last_hero_lock_target = None
        self.lives_var.set("—")
        self.monitor_var.set(f"实时监视：正在读取玩家 {self.selected_player + 1}")
        self.result_var.set(f"操作对象：玩家 {self.selected_player + 1}")
        if self.shared_view:
            self.villager_ai_var.set(read_shared_uint64(
                player_view(self.shared_view, self.selected_player), VILLAGER_AI_DISABLED_OFFSET) == 0)
            self._refresh_hero_controls(enabled=self.hero_controls_enabled)

    def _toggle_villager_ai(self) -> None:
        if not self.shared_view:
            self.villager_ai_var.set(True)
            self.result_var.set("请先连接游戏，再设置村民 AI")
            return
        set_villager_ai(self.shared_view, self.villager_ai_var.get(), self.selected_player)
        self.result_var.set(f"玩家 {self.selected_player + 1}：已发送村民 AI "
                            + ("强化指令" if self.villager_ai_var.get() else "恢复原生指令"))

    def start_connect(self) -> None:
        if self.connecting:
            return
        self.connecting = True
        self.connect_button.configure(state="disabled")
        self.progress.configure(mode="indeterminate", value=0)
        self.progress.start(12)
        self.connection_var.set("正在连接游戏……")

        def worker() -> None:
            ready_event = 0
            shared_handle = 0
            shared_view = 0
            try:
                pid = mono_injector.find_process(PROCESS_NAME)
                ready_event = create_ready_event(pid)
                # Bootstrap.Start is idempotent. The window hook exists only
                # long enough to create the Unity main-thread monitor.
                mono_injector.inject_managed_bridge(pid, bundled_bridge_path())
                deadline = time.monotonic() + 18.0
                while kernel32.WaitForSingleObject(ready_event, 100) != WAIT_OBJECT_0:
                    if time.monotonic() >= deadline:
                        raise mono_injector.InjectorError(self._latest_bridge_error(pid))
                game_window = find_game_window(pid)
                if not game_window:
                    raise mono_injector.InjectorError("桥接已加载，但找不到游戏窗口。")
                shared_handle, shared_view = open_shared_state(pid)
                if not monitor_is_ready(shared_view):
                    if not install_lives_monitor(game_window):
                        raise mono_injector.InjectorError(self._latest_bridge_error(pid))
                    monitor_deadline = time.monotonic() + 3.0
                    while not monitor_is_ready(shared_view):
                        if time.monotonic() >= monitor_deadline:
                            raise mono_injector.InjectorError("游戏主线程监视器启动超时。")
                        time.sleep(0.05)
                self.root.after(
                    0,
                    lambda: self._connect_finished(
                        pid,
                        ready_event,
                        game_window,
                        shared_handle,
                        shared_view,
                        None,
                    ),
                )
            except Exception as exception:
                close_shared_state(shared_handle, shared_view)
                self.root.after(
                    0,
                    lambda error=exception, event=ready_event: self._connect_finished(
                        None, event, 0, 0, 0, error
                    ),
                )

        threading.Thread(target=worker, daemon=True).start()

    def _latest_bridge_error(self, pid: int) -> str:
        try:
            state, status_pid, details = parse_bridge_status(STATUS_PATH.read_text(encoding="utf-8-sig"))
            if status_pid == pid and state == "ERROR" and details:
                return details[0]
        except (OSError, UnicodeError):
            pass
        return "等待游戏桥接响应超时，请进入主菜单后重试。"

    def _connect_finished(
        self,
        pid: int | None,
        ready_event: int,
        game_window: int,
        shared_handle: int,
        shared_view: int,
        error: Exception | None,
    ) -> None:
        self.connecting = False
        self.connect_button.configure(state="normal")
        self.progress.stop()
        if error is not None:
            self.progress.configure(mode="determinate", value=0)
            if ready_event:
                kernel32.CloseHandle(ready_event)
            self._clear_connection("连接失败")
            self.result_var.set(str(error))
            messagebox.showerror("Broforce 命数 / 人物修改器", str(error))
            return

        self._close_ready_event()
        self._close_shared_state()
        self.pid = pid
        self.ready_event = ready_event
        self.game_window = game_window
        self.shared_handle = shared_handle
        self.shared_view = shared_view
        self.villager_ai_var.set(read_shared_uint64(
            player_view(shared_view, self.selected_player), VILLAGER_AI_DISABLED_OFFSET) == 0)
        self.progress.configure(mode="determinate", value=100)
        self.last_monitor_heartbeat = read_monitor_heartbeat(shared_view)
        self.last_monitor_heartbeat_time = time.monotonic()
        self.connection_var.set(f"已连接（PID {pid}）")
        self.result_var.set("已就绪：F6 普通加命｜F7 钢铁兄弟加命")
        self.monitor_var.set("实时监视：正在读取……")
        self.add_button.configure(state="normal")
        self.ironbro_add_button.configure(state="normal")
        self.spawn_villager_button.configure(state="normal")
        self.spawn_companion_button.configure(state="normal")
        self._refresh_hero_controls()

    def add_life(self) -> None:
        if not self.pid or not self.shared_view:
            self.start_connect()
            return
        request_add_life(self.shared_view, self.selected_player)
        self.result_var.set("已发送 +1 命指令……")

    def add_ironbro_life(self) -> None:
        if not self.pid or not self.shared_view:
            self.start_connect()
            return
        request_ironbro_add_life(self.shared_view, self.selected_player)
        self.result_var.set("已发送钢铁兄弟正常加命指令……")

    def spawn_villager(self) -> None:
        if not self.pid or not self.shared_view:
            self.result_var.set("请先连接游戏")
            return
        if not monitor_is_ready(self.shared_view):
            self.result_var.set("游戏正在加载，请稍后再生成")
            return
        request_spawn_villager(self.shared_view, self.selected_player)
        self.result_var.set("已发送生成武装平民指令……")

    def spawn_companion(self) -> None:
        if not self.pid or not self.shared_view:
            self.result_var.set("请先连接游戏")
            return
        if not monitor_is_ready(self.shared_view):
            self.result_var.set("游戏正在加载，请稍后再生成")
            return
        hero_type = self.hero_display_to_id.get(self.hero_choice_var.get())
        if hero_type is None or not read_unlocked_hero_mask(self.shared_view) & (1 << hero_type):
            self.result_var.set("请先在人物列表或头像选人中选择已解锁人物")
            return
        request_spawn_companion(self.shared_view, hero_type, self.selected_player)
        self.result_var.set(f"已发送生成电脑队友指令：{HERO_NAMES_ZH[hero_type]}")

    def switch_hero(self) -> None:
        if not self.pid or not self.shared_view:
            self.result_var.set("请先连接游戏")
            return
        if not monitor_is_ready(self.shared_view):
            self.result_var.set("游戏正在加载，请稍后再切换")
            return
        hero_type = self.hero_display_to_id.get(self.hero_choice_var.get())
        if hero_type is None or not read_unlocked_hero_mask(self.shared_view) & (1 << hero_type):
            self.result_var.set("请先选择当前存档已解锁的人物")
            return
        request_hero_switch(self.shared_view, hero_type, self.selected_player)
        self.result_var.set(f"已发送单次切换指令：{HERO_NAMES_ZH[hero_type]}")

    def enable_hero_lock(self) -> None:
        if not self.pid or not self.shared_view:
            self.start_connect()
            return
        hero_type = self.hero_display_to_id.get(self.hero_choice_var.get())
        if hero_type is None:
            messagebox.showwarning("无法锁定人物", "尚未读取到当前存档的已解锁人物。")
            return
        mask = read_unlocked_hero_mask(self.shared_view)
        if not mask & (1 << hero_type):
            messagebox.showwarning("无法锁定人物", "所选人物尚未在当前存档中解锁。")
            return
        request_hero_lock(self.shared_view, hero_type, self.selected_player)
        self.hero_lock_var.set(f"人物锁定：正在启用｜{HERO_NAMES_ZH[hero_type]}")
        self.result_var.set(f"已发送人物锁定指令：{HERO_NAMES_ZH[hero_type]}")
        self.hero_unlock_button.configure(state="normal")

    def disable_hero_lock(self) -> None:
        if not self.pid or not self.shared_view:
            return
        request_hero_lock(self.shared_view, -1, self.selected_player)
        self.hero_lock_var.set("人物锁定：正在关闭……")
        self.result_var.set("已发送关闭人物锁定指令")

    def _connection_tick(self) -> None:
        try:
            current_pid = mono_injector.find_process(PROCESS_NAME)
        except mono_injector.InjectorError:
            if self.pid is not None:
                self._clear_connection()
            self.root.after(900, self._connection_tick)
            return

        if self.pid is not None and current_pid != self.pid:
            self._clear_connection("检测到游戏已重启，请重新连接")
        elif self.pid is not None and self.ready_event:
            if kernel32.WaitForSingleObject(self.ready_event, 0) != WAIT_OBJECT_0:
                self._clear_connection("桥接连接已失效")
        elif not self.connecting:
            self.connection_var.set("已发现 Broforce，点击连接")
        self.root.after(900, self._connection_tick)

    def _hotkey_tick(self) -> None:
        f6_down = bool(user32.GetAsyncKeyState(VK_F6) & 0x8000)
        f7_down = bool(user32.GetAsyncKeyState(VK_F7) & 0x8000)
        f8_down = bool(user32.GetAsyncKeyState(VK_F8) & 0x8000)
        f9_down = bool(user32.GetAsyncKeyState(VK_F9) & 0x8000)
        if self.pid and self.game_window:
            foreground = int(user32.GetForegroundWindow() or 0)
            if foreground == self.game_window:
                if f6_down and not self.was_f6_down:
                    self.add_life()
                if f7_down and not self.was_f7_down:
                    self.add_ironbro_life()
                if f8_down and not self.was_f8_down:
                    self.spawn_villager()
                if f9_down and not self.was_f9_down:
                    self.spawn_companion()
        self.was_f9_down = f9_down
        self.was_f8_down = f8_down
        self.was_f6_down = f6_down
        self.was_f7_down = f7_down
        self.root.after(30, self._hotkey_tick)

    def _status_tick(self) -> None:
        try:
            stat = STATUS_PATH.stat()
            if stat.st_mtime_ns != self.last_status_mtime:
                self.last_status_mtime = stat.st_mtime_ns
                state, pid, details = parse_bridge_status(STATUS_PATH.read_text(encoding="utf-8-sig"))
                if self.pid is not None and pid == self.pid:
                    self._apply_bridge_status(state, details)
        except (OSError, UnicodeError):
            pass
        self.root.after(250, self._status_tick)

    def _lives_tick(self) -> None:
        if self.pid is not None and self.shared_view:
            heartbeat = read_monitor_heartbeat(self.shared_view)
            now = time.monotonic()
            is_ready = monitor_is_ready(self.shared_view)
            if heartbeat != self.last_monitor_heartbeat:
                self.last_monitor_heartbeat = heartbeat
                self.last_monitor_heartbeat_time = now

            heartbeat_age = now - self.last_monitor_heartbeat_time
            health = classify_monitor_health(is_ready, heartbeat_age)
            if health == "RECOVER":
                self.monitor_var.set("实时监视：已中断，正在自动恢复……")
                self.add_button.configure(state="disabled")
                self.ironbro_add_button.configure(state="disabled")
                self._set_hero_controls_enabled(False)
                if not self.connecting:
                    self.root.after(0, self._recover_monitor)
                self.root.after(MONITOR_INTERVAL_MS, self._lives_tick)
                return
            if health == "BUSY":
                self.monitor_var.set("实时监视：切换关卡或游戏忙，正在等待恢复……")
                self.add_button.configure(state="disabled")
                self.ironbro_add_button.configure(state="disabled")
                self._set_hero_controls_enabled(False)
                self.root.after(MONITOR_INTERVAL_MS, self._lives_tick)
                return

            if not self.connecting:
                self.add_button.configure(state="normal")
                self.ironbro_add_button.configure(state="normal")
                self._refresh_hero_controls()
            state, player_index, lives = query_player_lives(self.shared_view, self.selected_player)
            if state == "OK" and player_index is not None and lives is not None:
                self.lives_var.set(str(lives))
                self.monitor_var.set(
                    f"实时监视：玩家 {player_index + 1}｜每 {MONITOR_INTERVAL_MS / 1000:.2f} 秒刷新"
                )
            elif state == "WAITING":
                self.lives_var.set("—")
                self.monitor_var.set(f"实时监视：玩家 {self.selected_player + 1} 尚未加入关卡")
            elif state == "ONLINE":
                self.lives_var.set("—")
                self.monitor_var.set("实时监视：在线房间不可用")
            else:
                self.monitor_var.set("实时监视：状态暂不可用")
        self.root.after(MONITOR_INTERVAL_MS, self._lives_tick)

    def _recover_monitor(self) -> None:
        if self.connecting:
            return
        self._clear_connection("监视中断，正在自动恢复……")
        self.result_var.set("正在重新建立 Unity 主线程监视器……")
        self.start_connect()

    def _apply_bridge_status(self, state: str, details: list[str]) -> None:
        if details and details[-1].startswith("PLAYER="):
            source_player = int(details[-1].split("=", 1)[1])
            details = details[:-1]
            if source_player != self.selected_player and state not in ("MONITOR", "ERROR"):
                return
        if state == "OK" and len(details) >= 2:
            player_number = int(details[0]) + 1
            self.lives_var.set(details[1])
            self.result_var.set(f"玩家 {player_number} 已 +1 命")
        elif state == "IRONBRO_OK" and len(details) >= 3:
            player_number = int(details[0]) + 1
            self.lives_var.set(details[1])
            character_result = ""
            if len(details) >= 5:
                action = "复活死亡角色" if details[3] == "REVIVED" else "加入新角色"
                character_result = f"，{action} {details[4]}"
            self.result_var.set(
                f"玩家 {player_number} 钢铁兄弟命数已 +1"
                f"（可用角色 {details[2]}{character_result}）"
            )
        elif state == "IRONBRO_REPAIRED" and len(details) >= 3:
            player_number = int(details[0]) + 1
            self.lives_var.set(details[1])
            character_result = ""
            if len(details) >= 5:
                action = "复活死亡角色" if details[3] == "REVIVED" else "加入新角色"
                character_result = f"，{action} {details[4]}"
            self.result_var.set(
                f"玩家 {player_number} 已修复 1 个幽灵命数"
                f"（可用角色 {details[2]}{character_result}）"
            )
        elif state == "HERO_SWITCH_OK" and len(details) >= 2:
            hero_name = HERO_NAMES_ZH[int(details[1])]
            self._refresh_hero_controls()
            self.result_var.set(f"玩家 {int(details[0]) + 1} 已切换为 {hero_name}（未锁定）")
        elif state == "HERO_SWITCH_DENIED" and details:
            self._refresh_hero_controls()
            self.result_var.set(details[0])
        elif state == "HERO_LOCKED" and len(details) >= 3:
            player_number = int(details[0]) + 1
            hero_name = details[2]
            self.hero_lock_var.set(f"人物锁定：已生效｜{hero_name}")
            self.result_var.set(f"玩家 {player_number} 已固定使用 {hero_name}")
            self._refresh_hero_controls()
        elif state == "HERO_LOCK_ARMED" and len(details) >= 2:
            hero_name = details[1]
            self.hero_lock_var.set(f"人物锁定：已开启｜{hero_name}（等待进入关卡）")
            self.result_var.set(f"已锁定 {hero_name}，进入普通单人关卡后自动应用")
            self._refresh_hero_controls()
        elif state == "HERO_LOCK_OFF":
            self.hero_lock_var.set("人物锁定：未开启")
            self.result_var.set("人物锁定已关闭")
            self._refresh_hero_controls()
        elif state == "HERO_LOCK_DENIED" and details:
            self.hero_lock_var.set("人物锁定：未开启")
            self.result_var.set(details[0])
            self._refresh_hero_controls()
            messagebox.showwarning("人物锁定已关闭", details[0])
        elif state in ("VILLAGER_OK", "VILLAGER_WAIT", "VILLAGER_DENIED", "COMPANION_OK", "COMPANION_DENIED") and details:
            self.result_var.set(details[0])
        elif state == "WAITING" and details:
            self.result_var.set(details[0])
        elif state == "DENIED" and details:
            self.result_var.set(details[0])
            messagebox.showwarning("已拒绝操作", details[0])
        elif state == "ERROR" and details:
            self.result_var.set(details[0])

    def _close(self) -> None:
        self._close_ready_event()
        self._close_shared_state()
        self.root.destroy()


def main() -> None:
    root = tk.Tk()
    TrainerApp(root)
    root.mainloop()


if __name__ == "__main__":
    main()
