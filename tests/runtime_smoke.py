"""Manual smoke test against a running Broforce process."""

from pathlib import Path
import sys
import time


PROJECT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(PROJECT))

import mono_injector
import trainer_gui


def main() -> int:
    pid = mono_injector.find_process()
    ready = trainer_gui.create_ready_event(pid)
    shared_handle = 0
    shared_view = 0
    try:
        # Start is idempotent and uses a one-shot hook only for initialization.
        mono_injector.inject_managed_bridge(pid, PROJECT / "managed" / "BroforceLifeBridge.dll")

        deadline = time.monotonic() + 20.0
        while mono_injector.kernel32.WaitForSingleObject(ready, 100) != trainer_gui.WAIT_OBJECT_0:
            if time.monotonic() >= deadline:
                raise RuntimeError("bridge ready timeout")

        window = trainer_gui.find_game_window(pid)
        if not window:
            raise RuntimeError("game window not found")
        print(f"READY pid={pid} hwnd=0x{window:X}")

        shared_handle, shared_view = trainer_gui.open_shared_state(pid)
        if not trainer_gui.monitor_is_ready(shared_view):
            if not trainer_gui.install_lives_monitor(window):
                raise RuntimeError("monitor installation failed")
            monitor_deadline = time.monotonic() + 3.0
            while not trainer_gui.monitor_is_ready(shared_view):
                if time.monotonic() >= monitor_deadline:
                    raise RuntimeError("monitor ready timeout")
                time.sleep(0.05)

        heartbeat_before = trainer_gui.read_monitor_heartbeat(shared_view)
        time.sleep(0.7)
        heartbeat_after = trainer_gui.read_monitor_heartbeat(shared_view)
        print(f"HEARTBEAT before={heartbeat_before} after={heartbeat_after}")
        if heartbeat_after == heartbeat_before:
            raise RuntimeError("monitor heartbeat did not advance")

        state, player_index, lives = trainer_gui.query_player_lives(shared_view)
        print(f"MONITOR state={state} player={player_index} lives={lives}")
        if state not in {"OK", "WAITING"}:
            raise RuntimeError(f"unexpected monitor state: {state}")

        # At the main menu this is a no-op, but it proves the shared command
        # counter is consumed from MonoBehaviour.Update without changing lives.
        if state == "WAITING":
            try:
                previous_mtime = trainer_gui.STATUS_PATH.stat().st_mtime_ns
            except OSError:
                previous_mtime = 0
            trainer_gui.request_add_life(shared_view)
            command_deadline = time.monotonic() + 3.0
            while time.monotonic() < command_deadline:
                try:
                    stat = trainer_gui.STATUS_PATH.stat()
                    text = trainer_gui.STATUS_PATH.read_text(encoding="utf-8-sig")
                    command_state, command_pid, details = trainer_gui.parse_bridge_status(text)
                except OSError:
                    time.sleep(0.05)
                    continue
                if stat.st_mtime_ns != previous_mtime and command_pid == pid:
                    print(f"COMMAND state={command_state} details={details}")
                    if command_state != "WAITING":
                        raise RuntimeError(f"unexpected no-op command state: {command_state}")
                    break
                time.sleep(0.05)
            else:
                raise RuntimeError("shared command was not consumed by Unity Update")
        return 0
    finally:
        trainer_gui.close_shared_state(shared_handle, shared_view)
        mono_injector.kernel32.CloseHandle(ready)


if __name__ == "__main__":
    raise SystemExit(main())
