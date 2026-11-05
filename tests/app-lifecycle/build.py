"""Build an original short-lived NRO for full-emulator lifecycle checks."""
import argparse
from pathlib import Path
import struct
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="ryujinx-exit-guest-") as temporary:
        obj = Path(temporary) / "exit.o"
        subprocess.run(["clang", "-target", "aarch64-none-elf", "-c",
                        str(Path(__file__).with_name("exit.S")), "-o", str(obj)], check=True)
        data = obj.read_bytes()
    header = struct.unpack_from("<16sHHIQQQIHHHHHH", data)
    assert header[0][:6] == b"\x7fELF\x02\x01" and header[2] == 183
    sections = [struct.unpack_from("<IIQQQQIIQQ", data, header[6] + i * header[11])
                for i in range(header[12])]
    names_header = sections[header[13]]
    names = data[names_header[4]:names_header[4] + names_header[5]]
    nro = None
    for section in sections:
        assert section[1] not in (4, 9) or section[5] == 0, "Unexpected relocations"
        if names[section[0]:].split(b"\0", 1)[0] == b".text":
            nro = data[section[4]:section[4] + section[5]]
    assert nro is not None and len(nro) == 0x3038
    assert nro[0x10:0x14] == b"NRO0" and nro[0x3000:0x3004] == b"ASET"
    args.output.write_bytes(nro)


if __name__ == "__main__":
    main()
