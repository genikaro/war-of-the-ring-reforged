"""Bounds-checked little-endian reads shared by the presentation asset formats."""
import math
import struct


class Reader:
    def __init__(self, data):
        self.data, self.pos = data, 0

    def take(self, size):
        if size < 0 or size > len(self.data) - self.pos:
            raise ValueError('truncated asset at 0x%x (need %d bytes)' % (self.pos, size))
        start = self.pos
        self.pos += size
        return self.data[start:self.pos]

    def read(self, fmt):
        values = struct.unpack('<' + fmt, self.take(struct.calcsize('<' + fmt)))
        if any(isinstance(v, float) and not math.isfinite(v) for v in values):
            raise ValueError('non-finite asset parameter at 0x%x' % self.pos)
        return values[0] if len(values) == 1 else list(values)

    def count(self, stride=4):
        value = self.read('I')
        if value > (len(self.data) - self.pos) // stride:
            raise ValueError('asset count exceeds remaining data at 0x%x' % (self.pos - 4))
        return value

    def expect(self, value):
        got = self.read('I')
        if got != value:
            raise ValueError('expected %d at 0x%x, got %d' % (value, self.pos - 4, got))

    def version(self, allowed):
        value = round(self.read('f'), 4)
        if value not in allowed:
            raise ValueError('unsupported version %s at 0x%x' % (value, self.pos - 4))
        return value

    def header(self, magic, versions):
        if self.take(8) != magic:
            raise ValueError('not a %s asset' % magic.decode('ascii'))
        return self.version(versions)

    def string(self):
        if self.read('B') != 0:
            raise ValueError('invalid asset string marker')
        raw = self.take(2 * self.count(2))
        if not raw.endswith(b'\0\0'):
            raise ValueError('unterminated asset string')
        return raw[:-2].decode('utf-16-le')

    def finish(self):
        if self.pos != len(self.data):
            raise ValueError('%d trailing asset bytes at 0x%x' % (len(self.data) - self.pos, self.pos))
