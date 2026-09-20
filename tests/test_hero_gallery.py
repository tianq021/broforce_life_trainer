import ctypes
import tkinter as tk
import unittest

import trainer_gui as gui


class HeroGalleryTests(unittest.TestCase):
    def setUp(self):
        self.root = tk.Tk()
        self.root.withdraw()
        self.app = gui.TrainerApp(self.root)
        self.state = ctypes.create_string_buffer(gui.SHARED_MEMORY_SIZE)
        self.app.shared_view = ctypes.addressof(self.state)
        self.app.pid = 1
        self.mask = ctypes.c_uint64.from_buffer(self.state, gui.UNLOCKED_HERO_MASK_OFFSET)
        self.target = ctypes.c_int64.from_buffer(self.state, gui.HERO_LOCK_TARGET_OFFSET)
        self.mask.value = (1 << 52) - 1
        self.target.value = -1
        self.app._refresh_hero_controls()
        self.app._open_hero_gallery()

    def tearDown(self):
        self.app.shared_view = 0
        self.root.destroy()

    def test_portrait_click_and_lock_use_correct_hero(self):
        self.assertEqual(len(self.app.hero_cards), 52)
        for hero_id, card in self.app.hero_cards.items():
            self.assertTrue(card.cget('image'))
            card.invoke()
            self.assertEqual(self.app.hero_display_to_id[self.app.hero_choice_var.get()], hero_id)
            self.assertEqual(card.cget('relief'), 'sunken')
            self.assertEqual(self.target.value, -1)
        self.app.hero_cards[17].invoke()
        self.app.gallery_lock_button.invoke()
        self.assertEqual(self.target.value, 17)
        self.app._refresh_hero_controls()
        self.assertIn('[已锁定]', self.app.hero_cards[17].cget('text'))
        self.app.hero_cards[0].invoke()
        self.app._refresh_hero_controls()
        self.assertEqual(self.app.hero_choice_var.get(), gui.hero_choice_label(0))
        self.assertIn('[已锁定]', self.app.hero_cards[17].cget('text'))

    def test_switch_is_separate_from_lock_and_repeatable(self):
        ctypes.c_uint64.from_buffer(self.state, gui.MONITOR_READY_OFFSET).value = 1
        command = ctypes.c_uint64.from_buffer(self.state, gui.HERO_SWITCH_COMMAND_OFFSET)
        self.app.hero_cards[17].invoke()
        self.app.gallery_switch_button.invoke()
        first = command.value
        self.assertEqual(first & 255, 17)
        self.assertEqual(self.target.value, -1)
        self.app.hero_switch_button.invoke()
        self.assertNotEqual(command.value, first)
        self.app.gallery_lock_button.invoke()
        self.assertEqual(self.target.value, 17)
        previous = command.value
        self.app._set_hero_controls_enabled(False)
        self.app.gallery_switch_button.invoke()
        self.app.hero_switch_button.invoke()
        self.assertEqual(command.value, previous)

    def test_switch_rejects_loading_and_stale_selection(self):
        command = ctypes.c_uint64.from_buffer(self.state, gui.HERO_SWITCH_COMMAND_OFFSET)
        self.app.hero_cards[17].invoke()
        self.app.switch_hero()
        self.assertEqual(command.value, 0)
        ctypes.c_uint64.from_buffer(self.state, gui.MONITOR_READY_OFFSET).value = 1
        self.mask.value = 1
        self.app.switch_hero()
        self.assertEqual(command.value, 0)

    def test_companion_uses_portrait_selection_without_switching_or_locking_player(self):
        ctypes.c_uint64.from_buffer(self.state, gui.MONITOR_READY_OFFSET).value = 1
        for hero in (4, 17, 51):
            self.app.hero_cards[hero].invoke()
            self.app.spawn_companion()
            command = gui.read_shared_uint64(self.app.shared_view, gui.SPAWN_COMPANION_SEQUENCE_OFFSET)
            self.assertEqual(command & 255, hero)
            self.assertEqual(self.target.value, -1)
            self.assertEqual(gui.read_shared_uint64(self.app.shared_view, gui.HERO_SWITCH_COMMAND_OFFSET), 0)
        previous = command
        self.mask.value = 1
        self.app.spawn_companion()
        self.assertEqual(gui.read_shared_uint64(self.app.shared_view, gui.SPAWN_COMPANION_SEQUENCE_OFFSET), previous)

    def test_disabled_and_changed_unlock_list(self):
        self.app._set_hero_controls_enabled(False)
        self.app.hero_cards[17].invoke()
        self.app.gallery_lock_button.invoke()
        self.assertEqual(self.target.value, -1)
        self.mask.value = 1 << 5
        self.app._refresh_hero_controls()
        self.assertEqual(list(self.app.hero_cards), [5])
        self.app.hero_cards[5].invoke()
        self.assertEqual(self.app.hero_choice_var.get(), gui.hero_choice_label(5))
        self.mask.value = 0
        self.app._refresh_hero_controls()
        self.assertFalse(self.app.hero_cards)
        self.assertEqual(str(self.app.gallery_lock_button['state']), 'disabled')
        self.app.hero_gallery.destroy()
        self.mask.value = 3
        self.app._refresh_hero_controls()
        self.app._open_hero_gallery()
        self.assertEqual(list(self.app.hero_cards), [0, 1])


if __name__ == '__main__':
    unittest.main()
