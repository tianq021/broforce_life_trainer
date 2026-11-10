# -*- mode: python ; coding: utf-8 -*-
from pathlib import Path

project = Path(SPECPATH)

a = Analysis(
    [str(project / "trainer_gui.py")],
    pathex=[str(project)],
    binaries=[],
    datas=[
        (str(project / "managed" / "BroforceLifeBridge.dll"), "."),
        (str(project / "assets" / "hero_portraits"), "assets/hero_portraits"),
    ],
    hiddenimports=[],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.datas,
    [],
    name="Broforce_加一命修改器_V14",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)
