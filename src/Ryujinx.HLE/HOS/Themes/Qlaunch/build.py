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

print(f'{len(code)} bytes, {count} statements')
