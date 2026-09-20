import ctypes
import tkinter as tk
import unittest

import trainer_gui as gui


class VillagerAiControlsTests(unittest.TestCase):
    def test_toggle_does_not_change_other_players_or_commands(self):
        memory = ctypes.create_string_buffer(gui.SHARED_MEMORY_SIZE)
        view = ctypes.addressof(memory)
        gui.request_spawn_villager(view, 2)
        gui.set_villager_ai(view, False, 2)
        self.assertEqual([gui.read_shared_uint64(gui.player_view(view, i),
                         gui.VILLAGER_AI_DISABLED_OFFSET) for i in range(4)], [0, 0, 1, 0])
        self.assertEqual(gui.read_shared_uint64(gui.player_view(view, 2),
                         gui.SPAWN_VILLAGER_SEQUENCE_OFFSET), 1)
        gui.set_villager_ai(view, True, 2)
        self.assertEqual(gui.read_shared_uint64(gui.player_view(view, 2),
                         gui.VILLAGER_AI_DISABLED_OFFSET), 0)
        before = memory.raw
        with self.assertRaises(ValueError):
            gui.set_villager_ai(view, False, 4)
        self.assertEqual(memory.raw, before)

    def test_selection_recalls_each_players_switch(self):
        root = tk.Tk()
        root.withdraw()
        app = gui.TrainerApp(root)
        memory = ctypes.create_string_buffer(gui.SHARED_MEMORY_SIZE)
        app.shared_view = ctypes.addressof(memory)
        try:
            app.villager_ai_var.set(False)
            app._toggle_villager_ai()
            app.player_choice_var.set('玩家 2')
            app._on_player_selected()
            self.assertTrue(app.villager_ai_var.get())
            app.player_choice_var.set('玩家 1')
            app._on_player_selected()
            self.assertFalse(app.villager_ai_var.get())
        finally:
            app.shared_view = 0
            root.destroy()
