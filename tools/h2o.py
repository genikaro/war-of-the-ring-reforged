"""Reader/extractor for Liquid Entertainment H2O archives (version 6).

Layout (little endian):
    text banner terminated by "\\r\\n\\x1a"
    u32 version, u32 file_count, u64 archived_size, u64 raw_size
    file_count x 40-byte records:
        u32 flags, i32 dir_index, i32 name_index, u32 file_id,
        u32 raw_size, u32 comp_size, u64 offset, u32 crc32, u32 unknown
    blob: directory path table
    blob: file name table
    file blobs

Records with flags bit 0 set point at a blob: u32 comp_size, u32 raw_size,
u32 crc32 (zlib), then DCL-imploded data (stored if the sizes match). Other
records point straight at raw file data. String tables are always blobs. String tables are
u32 count, u32 byte_size, then NUL-terminated UTF-16LE strings.
Records with name_index == -1 are empty slots.

Usage:
    python h2o.py list <archive.H2O>
    python h2o.py extract <archive.H2O|archives dir> <out dir> [--verify]
"""
import os
import struct
import sys
import time
import zlib
from dataclasses import dataclass

import blast


@dataclass
class Entry:
    path: str
    raw_size: int
    comp_size: int
    offset: int
    crc: int
    packed: bool


class H2OArchive:
    def __init__(self, path):
        self.path = path
        with open(path, 'rb') as f:
            self.data = f.read()
        base = self.data.index(b'\r\n\x1a') + 3
        version, count, _, _ = struct.unpack_from('<IIQQ', self.data, base)
        if version != 6:
            raise ValueError('%s: unsupported H2O version %d' % (path, version))
        records = [struct.unpack_from('<IiiIIIQII', self.data, base + 24 + 40 * i)
                   for i in range(count)]
        pos = base + 24 + 40 * count
        dirs, pos = self._strings(pos)
        names, pos = self._strings(pos)
        self.entries = []
        for flags, dir_index, name_index, _id, raw, comp, offset, crc, _ in records:
            if name_index < 0:
                continue
            name = names[name_index]
            if dir_index >= 0:
                name = dirs[dir_index] + '\\' + name
            self.entries.append(Entry(name, raw, comp, offset, crc, bool(flags & 1)))

    def _blob(self, offset):
        comp, raw, crc = struct.unpack_from('<III', self.data, offset)
        payload = self.data[offset + 12:offset + 12 + comp]
        out = payload if comp == raw else blast.explode(payload, raw)
        return out, offset + 12 + comp, crc

    def _strings(self, offset):
        blob, end, _ = self._blob(offset)
        count, _ = struct.unpack_from('<II', blob, 0)
        strings = blob[8:].decode('utf-16-le').split('\0')[:count]
        return strings, end

    def read(self, entry, verify=False):
        if entry.packed:
            out, _, crc = self._blob(entry.offset)
        else:
            out, crc = self.data[entry.offset:entry.offset + entry.raw_size], entry.crc
        if len(out) != entry.raw_size:
            raise ValueError('%s: size %d != %d' % (entry.path, len(out), entry.raw_size))
        if verify and zlib.crc32(out) != crc:
            raise ValueError('%s: CRC mismatch' % entry.path)
        return out


def extract(archive_path, out_root, verify=False, progress=None):
    """Every entry of one archive into out_root/<archive name>/. `progress(entry)` is called after each entry."""
    arc = H2OArchive(archive_path)
    dest = os.path.join(out_root, os.path.splitext(os.path.basename(archive_path))[0])
    start = time.time()
    for entry in arc.entries:
        target = os.path.join(dest, *entry.path.split('\\'))
        os.makedirs(os.path.dirname(target), exist_ok=True)
        with open(target, 'wb') as f:
            f.write(arc.read(entry, verify))
        if progress:
            progress(entry)
    print('%-22s %5d files  %7.1f MB  %5.1fs' % (
        os.path.basename(archive_path), len(arc.entries),
        sum(e.raw_size for e in arc.entries) / 1e6, time.time() - start), flush=True)


def main(argv):
    if len(argv) >= 2 and argv[0] == 'list':
        for e in H2OArchive(argv[1]).entries:
            print('%10d %10d  %s' % (e.raw_size, e.comp_size, e.path))
    elif len(argv) >= 3 and argv[0] == 'extract':
        src, out = argv[1], argv[2]
        verify = '--verify' in argv
        if os.path.isdir(src):
            paths = sorted(os.path.join(src, n) for n in os.listdir(src)
                           if n.lower().endswith('.h2o'))
        else:
            paths = [src]
        for p in paths:
            extract(p, out, verify)
    else:
        print(__doc__)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
