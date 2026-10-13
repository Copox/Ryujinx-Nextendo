# [Nextendo, HOME Menu themes] Assembles theme_page.s (the code added to the HOME Menu, qlaunch
# 22.5.0, build id 26CC3BC5...) into theme_page.bin, at the place the emulator puts it: right
# after the program's image, at CAVE (an offset from the module's base). Every reference is
# relative to the program counter, so the bytes hold wherever the module is loaded.
#
# The code starts with a table of branches, one per entry point (see theme_page.s); the emulator
# points the program at CAVE + 4 * n.
#
#   python build.py        (needs keystone-engine)
import os
import sys

from keystone import Ks, KS_ARCH_ARM64, KS_MODE_LITTLE_ENDIAN

CAVE = 0x425F000      # end of the image (bss end 0x425E008, page aligned)
DATA = CAVE + 0x8000  # the data page, written by the emulator before the program starts

here = os.path.dirname(os.path.abspath(__file__))
source = open(os.path.join(here, 'theme_page.s'), encoding='utf-8').read()
source = source.replace('@DATA', '#' + hex(DATA))

# Keystone wants plain lines; ';' starts a comment in the source.
lines = []
for line in source.splitlines():
    line = line.split(';', 1)[0].rstrip()
    if line.strip():
        lines.append(line)

ks = Ks(KS_ARCH_ARM64, KS_MODE_LITTLE_ENDIAN)
code, count = ks.asm('\n'.join(lines), CAVE)
code = bytes(code)

if len(code) > DATA - CAVE:
    sys.exit('code larger than its place')

open(os.path.join(here, 'theme_page.bin'), 'wb').write(code)

# The name of the "Ryujinx | Nextendo" button, at the end of the code.
LABEL = CAVE + code.index(b'Nx_SetNav_Ryujinx' + bytes(1))

# Changes to the program's own code, assembled at their place: (address, instructions).
ENTRY = {'index_of_button': CAVE + 0x10, 'index_of_focus': CAVE + 0x14, 'get_focused': CAVE + 0x18,
         'pick': CAVE + 0x1C, 'current': CAVE + 0x20, 'entry_button': CAVE + 0x24, 'main_poll': CAVE + 0x28, 'footer_focus': CAVE + 0x2C,
         'settings_scene': CAVE + 0x30, 'themes_scene': CAVE + 0x34, 'vgc_welcome': CAVE + 0x48}
PATCHES = [
    # the page's buttons: room for the added entries (it reserves 2)
    (0x98CEE0, ['mov w1, #18']),
    # focus callback: the focused entry, whichever it is
    (0x98DD90, ['mov x19, x0', f'bl #{ENTRY["index_of_focus"]:#x}', 'tbnz w0, #31, #0x98ddb4',
                'mov w2, w0', 'str w2, [x19, #0xd64]', f'bl #{ENTRY["footer_focus"]:#x}', 'b #0x98ddd0']),
    # check group callback: the picked entry, whichever it is
    (0x98DFD0, ['mov x0, x19', f'bl #{ENTRY["index_of_button"]:#x}', 'tbnz w0, #31, #0x98df34',
                'mov w20, w0', 'b #0x98dff4']),
    # ... a theme is drawn on Basic Black; the emulator is told which
    (0x98E058, [f'bl #{ENTRY["pick"]:#x}']),
    (0x98E064, ['str w0, [x8, #8]']),
    # page shown: the entry to check (the theme in use)
    (0x98E69C, [f'bl #{ENTRY["current"]:#x}', 'mov w8, w0', 'str w8, [x19, #0xd64]', 'b #0x98e6f8']),
    (0x98E6F8, ['tbnz w8, #31, #0x98e7a0', 'mov x0, x19', 'mov w1, w8', f'bl #{ENTRY["entry_button"]:#x}', 'mov x19, x0', 'cbz x0, #0x98e7a0']),
    # the focused button
    (0x98EEF0, [f'b #{ENTRY["get_focused"]:#x}']),
    # the main loop, once a frame after the keys: main_poll (it does "ldr x8, [x19]")
    (0xBB90A4, [f'bl #{ENTRY["main_poll"]:#x}']),
    # the settings menu: Bluetooth Audio (3) builds the themes page as "Ryujinx | Nextendo",
    # Themes (11) as the themes; its button named with a label added to setting.msbt
    (0x7DDF48, [f'bl #{ENTRY["settings_scene"]:#x}']),
    (0x7DDE28, [f'bl #{ENTRY["themes_scene"]:#x}']),
    (0x7DE620, [f'adrp x1, #{(LABEL & ~0xFFF):#x}', f'add x1, x1, #{(LABEL & 0xFFF):#x}']),
    # the virtual game cards page: its welcome only the first time
    (0xA5F478, [f'b #{ENTRY["vgc_welcome"]:#x}']),
]

with open(os.path.join(here, 'theme_page.patches'), 'wb') as f:
    f.write(len(PATCHES).to_bytes(4, 'little'))
    for address, asm in PATCHES:
        patch = bytes(ks.asm(chr(10).join(asm), address)[0])
        f.write(address.to_bytes(4, 'little') + len(patch).to_bytes(4, 'little') + patch)

print(f'{len(code)} bytes, {count} statements, {len(PATCHES)} patches')
