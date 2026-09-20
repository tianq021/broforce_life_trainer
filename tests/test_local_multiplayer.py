import ctypes
import tkinter as tk
import unittest
from unittest.mock import patch

import trainer_gui as gui


class LocalMultiplayerTests(unittest.TestCase):
    def setUp(self):
        self.state = ctypes.create_string_buffer(gui.SHARED_MEMORY_SIZE)
        self.view = ctypes.addressof(self.state)
        for player in range(4):
            gui.request_hero_lock(self.view, -1, player)

    def test_commands_and_snapshots_are_isolated_for_all_four_players(self):
        for player in range(4):
            base = self.view + player * gui.PLAYER_STRIDE
            ctypes.c_uint64.from_address(base + gui.LIVES_SNAPSHOT_OFFSET).value = ((player + 1) << 32) | (player + 10)
            gui.request_add_life(self.view, player)
            gui.request_ironbro_add_life(self.view, player)
            gui.request_spawn_villager(self.view, player)
            gui.request_hero_switch(self.view, player + 5, player)
            gui.request_hero_lock(self.view, player + 8, player)
            for other in range(4):
                self.assertEqual(gui.read_hero_lock_target(self.view, other), other + 8 if other <= player else -1)
                for offset in (gui.ADD_LIFE_SEQUENCE_OFFSET, gui.IRONBRO_ADD_LIFE_SEQUENCE_OFFSET, gui.SPAWN_VILLAGER_SEQUENCE_OFFSET):
                    self.assertEqual(gui.read_shared_uint64(self.view + other * gui.PLAYER_STRIDE, offset), int(other <= player))
        for player in range(4):
            self.assertEqual(gui.query_player_lives(self.view, player), ('OK', player, player + 10))
            command = gui.read_shared_uint64(self.view + player * gui.PLAYER_STRIDE, gui.HERO_SWITCH_COMMAND_OFFSET)
            self.assertEqual(command & 255, player + 5)
        gui.request_hero_lock(self.view, -1, 2)
        self.assertEqual([gui.read_hero_lock_target(self.view, i) for i in range(4)], [8, 9, -1, 11])
        self.assertEqual(gui.read_shared_uint64(self.view, gui.MONITOR_READY_OFFSET), 0)
        self.assertEqual(gui.read_shared_uint64(self.view, gui.UNLOCKED_HERO_MASK_OFFSET), 0)

    def test_invalid_player_does_not_write_memory(self):
        before = self.state.raw
        for player in (-1, 4):
            with self.assertRaises(ValueError):
                gui.request_hero_lock(self.view, 17, player)
        self.assertEqual(self.state.raw, before)

    def test_ui_selection_routes_actions_and_keeps_other_locks(self):
        root = tk.Tk()
        root.withdraw()
        app = gui.TrainerApp(root)
        app.pid = 1
        app.shared_view = self.view
        ctypes.c_uint64.from_address(self.view + gui.MONITOR_READY_OFFSET).value = 1
        ctypes.c_uint64.from_address(self.view + gui.UNLOCKED_HERO_MASK_OFFSET).value = (1 << 52) - 1
        app._refresh_hero_controls()
        try:
            for player in range(4):
                app.player_choice_var.set(f'玩家 {player + 1}')
                app._on_player_selected()
                app.hero_choice_var.set(gui.hero_choice_label(player + 10))
                app.enable_hero_lock()
                app.add_life()
                app.spawn_villager()
                app.switch_hero()
            self.assertEqual([gui.read_hero_lock_target(self.view, i) for i in range(4)], [10, 11, 12, 13])
            app.player_choice_var.set('玩家 2')
            app._on_player_selected()
            self.assertIn(gui.HERO_NAMES_ZH[11], app.hero_lock_var.get())
            app.disable_hero_lock()
            self.assertEqual([gui.read_hero_lock_target(self.view, i) for i in range(4)], [10, -1, 12, 13])
            app.result_var.set('player 2 status')
            app._apply_bridge_status('HERO_LOCKED', ['0', '10', 'other hero', 'PLAYER=0'])
            self.assertEqual(app.result_var.get(), 'player 2 status')
            for player in range(4):
                base = self.view + player * gui.PLAYER_STRIDE
                self.assertEqual(gui.read_shared_uint64(base, gui.ADD_LIFE_SEQUENCE_OFFSET), 1)
                self.assertEqual(gui.read_shared_uint64(base, gui.SPAWN_VILLAGER_SEQUENCE_OFFSET), 1)
                self.assertEqual(gui.read_shared_uint64(base, gui.HERO_SWITCH_COMMAND_OFFSET) & 255, player + 10)
        finally:
            app.shared_view = 0
            root.destroy()


if __name__ == '__main__':
    unittest.main()
