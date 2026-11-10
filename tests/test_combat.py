import ctypes
import tkinter as tk
import unittest
from types import SimpleNamespace
from unittest.mock import Mock, patch

import trainer_gui as gui


class CombatTests(unittest.TestCase):
    def setUp(self):
        self.memory = ctypes.create_string_buffer(gui.SHARED_MEMORY_SIZE)
        self.view = ctypes.addressof(self.memory)

    def test_commands_are_isolated_and_bad_players_never_write(self):
        for player in range(4):
            before = bytearray(self.memory.raw)
            gui.set_invincible(self.view, True, player)
            sequence = gui.request_refill_ammo(self.view, player)
            offset = player * gui.PLAYER_STRIDE
            before[offset + 88:offset + 96] = (1).to_bytes(8, 'little')
            before[offset + 96:offset + 104] = (1).to_bytes(8, 'little')
            self.assertEqual(sequence, 1)
            self.assertEqual(self.memory.raw, bytes(before))
        before = self.memory.raw
        for player in (-1, 4):
            with self.assertRaises(ValueError):
                gui.set_invincible(self.view, True, player)
            with self.assertRaises(ValueError):
                gui.request_refill_ammo(self.view, player)
        self.assertEqual(self.memory.raw, before)

    def test_close_clears_all_protection_and_heartbeat_without_touching_other_features(self):
        for player in range(4):
            gui.set_invincible(self.view, True, player)
            gui.request_hero_lock(self.view, 10 + player, player)
        gui.pulse_combat_client(self.view)
        gui.stop_combat_assists(self.view)
        self.assertEqual(gui.read_shared_uint64(self.view, gui.CLIENT_HEARTBEAT_OFFSET), 0)
        for player in range(4):
            self.assertEqual(gui.read_shared_uint64(gui.player_view(self.view, player), gui.INVINCIBLE_ENABLED_OFFSET), 0)
            self.assertEqual(gui.read_hero_lock_target(self.view, player), 10 + player)

    def test_sequence_rollover_and_nonzero_heartbeat(self):
        ctypes.c_uint64.from_address(self.view + gui.REFILL_AMMO_SEQUENCE_OFFSET).value = gui.REFILL_SEQUENCE_MASK
        self.assertEqual(gui.request_refill_ammo(self.view), 0)
        ctypes.c_uint64.from_address(self.view + gui.CLIENT_HEARTBEAT_OFFSET).value = gui.ULONG_PTR_MAX
        gui.pulse_combat_client(self.view)
        self.assertEqual(gui.read_shared_uint64(self.view, gui.CLIENT_HEARTBEAT_OFFSET), 1)

    def test_unready_gui_never_publishes_commands(self):
        app = SimpleNamespace(pid=1, shared_view=self.view, selected_player=0,
                              invincible_var=Mock(), result_var=Mock())
        gui.TrainerApp.toggle_invincible(app)
        gui.TrainerApp.refill_ammo(app)
        self.assertEqual(self.memory.raw, bytes(len(self.memory.raw)))
        app.invincible_var.set.assert_called_with(False)

    def test_f10_focus_and_hold_are_single_press(self):
        app = SimpleNamespace(pid=1, game_window=123, was_f6_down=False, was_f7_down=False,
                              was_f8_down=False, was_f9_down=False, was_f10_down=False,
                              root=Mock(), _hotkey_tick=Mock(), add_life=Mock(), add_ironbro_life=Mock(),
                              spawn_villager=Mock(), spawn_companion=Mock(), refill_ammo=Mock())
        with patch.object(gui.user32, 'GetAsyncKeyState', side_effect=lambda key: 0x8000 if key == gui.VK_F10 else 0), \
             patch.object(gui.user32, 'GetForegroundWindow', return_value=456) as foreground:
            gui.TrainerApp._hotkey_tick(app)
            foreground.return_value = 123
            gui.TrainerApp._hotkey_tick(app)
            app.refill_ammo.assert_not_called()
            app.was_f10_down = False
            gui.TrainerApp._hotkey_tick(app)
            gui.TrainerApp._hotkey_tick(app)
            app.refill_ammo.assert_called_once()

    def test_ui_player_switch_ack_and_close(self):
        root = tk.Tk()
        root.withdraw()
        app = gui.TrainerApp(root)
        app.pid = 1
        app.shared_view = self.view
        ctypes.c_uint64.from_address(self.view + gui.MONITOR_READY_OFFSET).value = 1
        try:
            app.invincible_var.set(True)
            app.toggle_invincible()
            app.refill_ammo()
            app.refill_ammo()  # Wait for acknowledgement instead of double-queueing.
            self.assertEqual(gui.read_shared_uint64(self.view, gui.REFILL_AMMO_SEQUENCE_OFFSET), 1)
            self.assertNotIn('已补满', app.result_var.get())
            app.player_choice_var.set('玩家 2')
            app._on_player_selected()
            self.assertFalse(app.invincible_var.get())
            app.invincible_var.set(True)
            app.toggle_invincible()
            app.refill_ammo()
            # P1 rejection must not overwrite P2's message.
            ctypes.c_uint64.from_address(self.view + gui.REFILL_AMMO_RESULT_OFFSET).value = (1 << 8) | 2
            before = app.result_var.get()
            app._combat_tick()
            self.assertEqual(app.result_var.get(), before)
            # Ignore a mismatched sequence, then display the current request's result.
            ack = self.view + gui.PLAYER_STRIDE + gui.REFILL_AMMO_RESULT_OFFSET
            ctypes.c_uint64.from_address(ack).value = (2 << 8) | 1
            app._combat_tick()
            self.assertEqual(app.pending_refills[1], 1)
            ctypes.c_uint64.from_address(ack).value = (1 << 8) | 1
            app._combat_tick()
            self.assertIn('玩家 2：手雷 / 特殊弹药已补满', app.result_var.get())
            self.assertIsNone(app.pending_refills[1])
            app.player_choice_var.set('玩家 1')
            app._on_player_selected()
            self.assertTrue(app.invincible_var.get())
            with patch.object(gui, 'close_shared_state'):
                app._close_shared_state()
            for player in range(4):
                self.assertEqual(gui.read_shared_uint64(gui.player_view(self.view, player), gui.INVINCIBLE_ENABLED_OFFSET), 0)
        finally:
            app.shared_view = 0
            root.destroy()


if __name__ == '__main__':
    unittest.main()
