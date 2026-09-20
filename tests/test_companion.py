import ctypes
import unittest
from types import SimpleNamespace
from unittest.mock import Mock, patch
import trainer_gui as gui


class CompanionTests(unittest.TestCase):
    def test_four_player_spawn_commands_do_not_touch_other_state(self):
        memory = ctypes.create_string_buffer(gui.SHARED_MEMORY_SIZE)
        view = ctypes.addressof(memory)
        for player in range(4):
            before = bytearray(memory.raw)
            gui.request_spawn_companion(view, player + 10, player)
            offset = player * gui.PLAYER_STRIDE + gui.SPAWN_COMPANION_SEQUENCE_OFFSET
            before[offset:offset + 8] = (256 | (player + 10)).to_bytes(8, 'little')
            self.assertEqual(memory.raw, bytes(before))
        before = memory.raw
        for player in (-1, 4):
            with self.assertRaises(ValueError):
                gui.request_spawn_companion(view, player + 10, player)
        self.assertEqual(memory.raw, before)

    def test_not_ready_does_not_queue_spawn(self):
        app = SimpleNamespace(pid=1, shared_view=123, selected_player=2, result_var=Mock(), hero_display_to_id={"chosen": 17}, hero_choice_var=Mock(get=Mock(return_value="chosen")))
        with patch.object(gui, 'monitor_is_ready', return_value=False), patch.object(gui, 'request_spawn_companion') as send:
            gui.TrainerApp.spawn_companion(app)
            send.assert_not_called()
        with patch.object(gui, 'monitor_is_ready', return_value=True), patch.object(gui, 'read_unlocked_hero_mask', return_value=1 << 17), patch.object(gui, 'request_spawn_companion') as send:
            gui.TrainerApp.spawn_companion(app)
            send.assert_called_once_with(123, 17, 2)

    def test_f9_held_outside_game_does_not_spawn_on_focus(self):
        app = SimpleNamespace(pid=1, game_window=123, was_f6_down=False, was_f7_down=False,
            was_f8_down=False, was_f9_down=False, root=Mock(), _hotkey_tick=Mock(),
            add_life=Mock(), add_ironbro_life=Mock(), spawn_villager=Mock(), spawn_companion=Mock())
        with patch.object(gui.user32, 'GetAsyncKeyState', side_effect=lambda key: 0x8000 if key == gui.VK_F9 else 0), patch.object(gui.user32, 'GetForegroundWindow', return_value=456) as foreground:
            gui.TrainerApp._hotkey_tick(app)
            foreground.return_value = 123
            gui.TrainerApp._hotkey_tick(app)
            app.spawn_companion.assert_not_called()

    def test_repeat_and_change_selection_are_atomic(self):
        memory = ctypes.create_string_buffer(gui.SHARED_MEMORY_SIZE)
        view = ctypes.addressof(memory)
        for sequence, hero in enumerate((17, 17, 51, 0), 1):
            gui.request_spawn_companion(view, hero, 3)
            self.assertEqual(gui.read_shared_uint64(gui.player_view(view, 3), gui.SPAWN_COMPANION_SEQUENCE_OFFSET), (sequence << 8) | hero)
        before = memory.raw
        for hero in (-1, 52, 255):
            with self.assertRaises(ValueError):
                gui.request_spawn_companion(view, hero)
        self.assertEqual(before, memory.raw)
