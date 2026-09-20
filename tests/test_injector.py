from pathlib import Path
import ctypes
import sys
import unittest


PROJECT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(PROJECT))

import mono_injector
import trainer_gui


class InjectorTests(unittest.TestCase):
    def test_stub_contains_success_return_and_is_small(self):
        names = (
            "mono_get_root_domain",
            "mono_thread_attach",
            "mono_domain_assembly_open",
            "mono_assembly_get_image",
            "mono_class_from_name",
            "mono_class_get_method_from_name",
            "mono_runtime_invoke",
        )
        functions = {name: 0x00007FF700001000 + index * 0x100 for index, name in enumerate(names)}
        pointers = {
            "path": 0x000001F000000400,
            "namespace": 0x000001F000000500,
            "class": 0x000001F000000600,
            "method": 0x000001F000000700,
            "exception": 0x000001F000000800,
        }
        stub = mono_injector._build_stub(functions, pointers)
        self.assertLess(len(stub), 1024)
        self.assertTrue(stub.endswith(b"\x48\x83\xC4\x20\x5B\xC3"))
        self.assertIn(b"\x31\xC0", stub)

    def test_status_parser(self):
        self.assertEqual(
            trainer_gui.parse_bridge_status("OK|1234|0|3"),
            ("OK", 1234, ["0", "3"]),
        )
        self.assertEqual(
            trainer_gui.parse_bridge_status("WAITING|1234|尚未进入关卡"),
            ("WAITING", 1234, ["尚未进入关卡"]),
        )

    def test_invalid_status_parser(self):
        self.assertEqual(trainer_gui.parse_bridge_status("broken"), ("INVALID", None, []))

    def test_lives_result_decoder(self):
        encoded = (2 << 32) | 7
        self.assertEqual(trainer_gui.decode_lives_result(encoded), ("OK", 1, 7))
        self.assertEqual(trainer_gui.decode_lives_result(0), ("WAITING", None, None))
        self.assertEqual(
            trainer_gui.decode_lives_result(trainer_gui.ULONG_PTR_MAX),
            ("ONLINE", None, None),
        )
        self.assertEqual(
            trainer_gui.decode_lives_result(5 << 32),
            ("INVALID", None, None),
        )

    def test_shared_state_reads_and_command_increment(self):
        shared = (ctypes.c_uint64 * 44)()
        shared[0] = (1 << 32) | 4
        shared[2] = 1
        shared[3] = 9
        shared[5] = trainer_gui.ULONG_PTR_MAX
        shared[6] = (1 << 0) | (1 << 17) | (1 << 51)
        address = ctypes.addressof(shared)

        self.assertTrue(trainer_gui.monitor_is_ready(address))
        self.assertEqual(trainer_gui.read_monitor_heartbeat(address), 9)
        self.assertEqual(trainer_gui.query_player_lives(address), ("OK", 0, 4))
        trainer_gui.request_add_life(address)
        self.assertEqual(shared[1], 1)
        trainer_gui.request_ironbro_add_life(address)
        self.assertEqual(shared[4], 1)
        trainer_gui.request_spawn_villager(address)
        self.assertEqual(shared[7], 1)
        self.assertEqual(shared[1], 1)
        self.assertEqual(shared[4], 1)
        self.assertEqual(trainer_gui.read_hero_lock_target(address), -1)
        trainer_gui.request_hero_lock(address, 17)
        self.assertEqual(trainer_gui.read_hero_lock_target(address), 17)
        self.assertEqual(
            trainer_gui.read_unlocked_hero_mask(address),
            (1 << 0) | (1 << 17) | (1 << 51),
        )
        self.assertEqual(trainer_gui.SHARED_MEMORY_SIZE, ctypes.sizeof(shared))

    def test_unlocked_hero_options_follow_mask(self):
        self.assertEqual(
            trainer_gui.hero_options_from_mask((1 << 0) | (1 << 17) | (1 << 51)),
            [
                (0, "Rambro"),
                (17, "Brochete"),
                (51, "DemolitionBro"),
            ],
        )

    def test_every_hero_has_a_bundled_portrait(self):
        portrait_paths = [
            trainer_gui.hero_portrait_path(hero_type)
            for hero_type in range(len(trainer_gui.HERO_NAMES))
        ]
        self.assertEqual(len(portrait_paths), 52)
        self.assertEqual(len(set(portrait_paths)), 52)
        for portrait_path in portrait_paths:
            self.assertTrue(portrait_path.is_file(), portrait_path)
            self.assertGreater(portrait_path.stat().st_size, 0)
            header = portrait_path.read_bytes()[:24]
            self.assertEqual(header[:8], b"\x89PNG\r\n\x1a\n")
            self.assertEqual(
                (int.from_bytes(header[16:20], "big"), int.from_bytes(header[20:24], "big")),
                (32, 48),
            )

        with self.assertRaises(ValueError):
            trainer_gui.hero_portrait_path(-1)
        with self.assertRaises(ValueError):
            trainer_gui.hero_portrait_path(len(trainer_gui.HERO_NAMES))

    def test_monitor_health_allows_level_transition_grace_period(self):
        self.assertEqual(trainer_gui.classify_monitor_health(True, 0.0), "HEALTHY")
        self.assertEqual(
            trainer_gui.classify_monitor_health(
                True, trainer_gui.MONITOR_STALL_NOTICE_SECONDS
            ),
            "BUSY",
        )
        self.assertEqual(
            trainer_gui.classify_monitor_health(
                True, trainer_gui.MONITOR_RECOVERY_SECONDS - 0.01
            ),
            "BUSY",
        )
        self.assertEqual(
            trainer_gui.classify_monitor_health(
                True, trainer_gui.MONITOR_RECOVERY_SECONDS
            ),
            "RECOVER",
        )
        self.assertEqual(trainer_gui.classify_monitor_health(False, 0.0), "RECOVER")


if __name__ == "__main__":
    unittest.main()
