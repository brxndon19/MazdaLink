#!/usr/bin/env python3
"""Rebuild MazdaLink's own RAM test and compare it with the embedded executable.

Requires GNU binutils-sh-elf. Never opens a driver, customer ROM or hardware.
"""
import hashlib
import pathlib
import re
import subprocess
import tempfile

root = pathlib.Path(__file__).resolve().parents[1]
source = root / "firmware" / "zyj2-ram-selftest.S"
linker = root / "firmware" / "zyj2-ram-selftest.ld"
policy = (root / "src" / "MazdaLink.Core" / "Zyj2RamSelfTest.cs").read_text()
literal = policy.split("byte[] image = Convert.FromHexString(", 1)[1].split(");", 1)[0]
embedded = bytes.fromhex("".join(re.findall(r'"([0-9A-F]+)"', literal)))
with tempfile.TemporaryDirectory(prefix="MazdaLink-ram-build-") as folder:
    folder = pathlib.Path(folder)
    obj, elf, binary = (folder / name for name in ["test.o", "test.elf", "test.bin"])
    subprocess.run(["sh-elf-as", "-big", "-isa=sh2", "-o", str(obj), str(source)], check=True)
    subprocess.run(["sh-elf-ld", "-T", str(linker), "-o", str(elf), str(obj)], check=True)
    subprocess.run(["sh-elf-objcopy", "-O", "binary", str(elf), str(binary)], check=True)
    actual = binary.read_bytes()
assert actual == embedded, "Assembled RAM program differs from embedded image"
assert len(actual) == 120
digest = hashlib.sha256(actual).hexdigest()
assert digest == "34bd2a95b30d2a34f8ccc6ccc5e3486cac2d7ecc5f8c31720e7a0a2594c1cad0"
print("PASS: own 120-byte SH-2 RAM test rebuilt exactly; sha256:" + digest)
