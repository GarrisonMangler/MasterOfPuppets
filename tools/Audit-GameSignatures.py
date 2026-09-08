#!/usr/bin/env python3
"""Count Master of Puppets byte signatures in an FFXIV executable."""

from __future__ import annotations

import argparse
from pathlib import Path
import struct


SIGNATURES = {
    "Chat.Send": "48 89 5C 24 ?? 48 89 74 24 10 57 48 83 EC 20 48 8B F2 48 8B F9 45 84 C9",
    "Chat.Sanitise": "E8 ?? ?? ?? ?? 48 8D 4C 24 ?? 0F B6 F8 E8 ?? ?? ?? ?? 48 8D 4D D0",
    "FollowStruct": "48 8D 15 ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 0F B6 47 3E",
    "PetPlace.bossmod": "E8 ?? ?? ?? ?? EB 3D 8B 93 ?? ?? ?? ??",
    "Movement.RMIWalk": "E8 ?? ?? ?? ?? 80 7B 3E 00 48 8D 3D",
    "Movement.RMIFly": "E8 ?? ?? ?? ?? 0F B6 0D ?? ?? ?? ?? B8",
    "Movement.InputActive": "E8 ?? ?? ?? ?? 84 C0 74 09 84 DB 74 1A",
    "Movement.WalkEnabled1": "E8 ?? ?? ?? ?? 84 C0 75 10 38 43 3C",
    "Movement.WalkEnabled2": "E8 ?? ?? ?? ?? 84 C0 75 03 88 47 3F",
    "Camera.RMICamera": "48 8B C4 53 48 81 EC ?? ?? ?? ?? 44 0F 29 50 ??",
    "Movement.PlayerMove": "E8 ?? ?? ?? ?? 4C 63 4B 04 48 8B 4E 28",
}


def find_all(data: bytes, signature: str) -> list[int]:
    tokens = signature.split()
    pattern = bytes(0 if token == "??" else int(token, 16) for token in tokens)
    mask = bytes(0 if token == "??" else 0xFF for token in tokens)
    first_fixed = next(index for index, value in enumerate(mask) if value)
    anchor = pattern[first_fixed]
    hits: list[int] = []
    start = 0
    while True:
        candidate = data.find(bytes([anchor]), start)
        if candidate < 0:
            return hits
        offset = candidate - first_fixed
        if offset >= 0 and offset + len(pattern) <= len(data):
            if all(not mask[index] or data[offset + index] == pattern[index]
                   for index in range(len(pattern))):
                hits.append(offset)
        start = candidate + 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("executable", type=Path)
    args = parser.parse_args()
    data = args.executable.read_bytes()
    failed = False
    for name, signature in SIGNATURES.items():
        hits = find_all(data, signature)
        status = "OK" if len(hits) == 1 else "FAIL"
        failed |= len(hits) != 1
        addresses = " ".join(f"0x{offset:X}" for offset in hits[:5])
        print(f"{status:4} {name:24} matches={len(hits)} {addresses}")
        if len(hits) > 1:
            for offset in hits[:5]:
                before = max(0, offset - 12)
                after = min(len(data), offset + len(signature.split()) + 28)
                print(f"     0x{before:X}: {data[before:after].hex(' ').upper()}")
                if data[offset] == 0xE8:
                    target = offset + 5 + struct.unpack_from("<i", data, offset + 1)[0]
                    print(f"     call target 0x{target:X}: {data[target:target + 32].hex(' ').upper()}")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
