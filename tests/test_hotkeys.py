import unittest
from unittest.mock import Mock, patch
from types import SimpleNamespace

import trainer_gui as gui


class HotkeyTests(unittest.TestCase):
    def test_separate_keys_edges_and_foreground(self):
        app = SimpleNamespace(
            pid=1, game_window=123, was_f6_down=False, was_f7_down=False,
            was_f8_down=False, was_f9_down=False, was_f10_down=False, root=Mock(), _hotkey_tick=Mock(),
            add_life=Mock(), add_ironbro_life=Mock(), spawn_villager=Mock(), spawn_companion=Mock(), refill_ammo=Mock())
        keys = set()
        with patch.object(gui.user32, 'GetAsyncKeyState', side_effect=lambda key: 0x8000 if key in keys else 0), patch.object(gui.user32, 'GetForegroundWindow', return_value=123) as foreground:
            for key, action in [(gui.VK_F6, app.add_life), (gui.VK_F7, app.add_ironbro_life), (gui.VK_F8, app.spawn_villager), (gui.VK_F9, app.spawn_companion), (gui.VK_F10, app.refill_ammo)]:
                keys.add(key)
                gui.TrainerApp._hotkey_tick(app)
                gui.TrainerApp._hotkey_tick(app)
                action.assert_called_once()
                keys.clear()
                gui.TrainerApp._hotkey_tick(app)
            keys.add(gui.VK_F8)
            foreground.return_value = 456
            gui.TrainerApp._hotkey_tick(app)
            foreground.return_value = 123
            gui.TrainerApp._hotkey_tick(app)
            app.spawn_villager.assert_called_once()
            keys.clear()
            gui.TrainerApp._hotkey_tick(app)
            keys.add(gui.VK_F8)
            gui.TrainerApp._hotkey_tick(app)
            self.assertEqual(app.spawn_villager.call_count, 2)

    def test_spawn_requires_ready_monitor(self):
        app = SimpleNamespace(pid=1, shared_view=123, selected_player=0, result_var=Mock())
        with patch.object(gui, 'monitor_is_ready', return_value=False), patch.object(gui, 'request_spawn_villager') as request:
            gui.TrainerApp.spawn_villager(app)
            request.assert_not_called()
        with patch.object(gui, 'monitor_is_ready', return_value=True), patch.object(gui, 'request_spawn_villager') as request:
            gui.TrainerApp.spawn_villager(app)
            request.assert_called_once_with(123, 0)
