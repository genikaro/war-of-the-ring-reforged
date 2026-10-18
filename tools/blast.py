"""PKWARE Data Compression Library "implode" decompressor (aka DCL explode).

Port of Mark Adler's blast.c (zlib contrib). Used by Liquid Entertainment
H2O archives. Huffman decoding goes through lookup tables for speed.
"""

# Compact run-length code-length tables from blast.c.
_LITLEN = bytes([
    11, 124, 8, 7, 28, 7, 188, 13, 76, 4, 10, 8, 12, 10, 12, 10, 8, 23, 8,
    9, 7, 6, 7, 8, 7, 6, 55, 8, 23, 24, 12, 11, 7, 9, 11, 12, 6, 7, 22, 5,
    7, 24, 6, 11, 9, 6, 7, 22, 7, 11, 38, 7, 9, 8, 25, 11, 8, 11, 9, 12,
    8, 12, 5, 38, 5, 38, 5, 11, 7, 5, 6, 21, 6, 10, 53, 8, 7, 24, 10, 27,
    44, 253, 253, 253, 252, 252, 252, 13, 12, 45, 12, 45, 12, 61, 12, 45,
    44, 173])
_LENLEN = bytes([2, 35, 36, 53, 38, 23])
_DISTLEN = bytes([2, 20, 53, 230, 247, 151, 248])
_BASE = (3, 2, 4, 5, 6, 7, 8, 9, 10, 12, 16, 24, 40, 72, 136, 264)
_EXTRA = (0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8)


def _build(rep):
    """Return (table, maxlen); table[peek] = (symbol << 4) | length."""
    lengths = []
    for b in rep:
        lengths.extend([b & 15] * ((b >> 4) + 1))
    maxlen = max(lengths)
    # Canonical codes, shortest first, symbols in order within a length.
    table = [0] * (1 << maxlen)
    code = 0
    for length in range(1, maxlen + 1):
        for sym, l in enumerate(lengths):
            if l != length:
                continue
            # Codes are stored bit-inverted, first bit read is the MSB.
            peek = 0
            for i in range(length):
                bit = ((code >> (length - 1 - i)) & 1) ^ 1
                peek |= bit << i
            entry = (sym << 4) | length
            for fill in range(1 << (maxlen - length)):
                table[peek | (fill << length)] = entry
            code += 1
        code <<= 1
    return table, maxlen


_LIT = _build(_LITLEN)
_LEN = _build(_LENLEN)
_DIST = _build(_DISTLEN)


class BlastError(Exception):
    pass


def explode(src, raw_size=None):
    """Decompress a DCL-imploded buffer. Stops at the end code or raw_size."""
    n = len(src)
    pos = 0
    buf = 0
    cnt = 0
    out = bytearray()

    def need(k):
        nonlocal pos, buf, cnt
        while cnt < k:
            byte = src[pos] if pos < n else 0
            pos += 1
            buf |= byte << cnt
            cnt += 8

    def bits(k):
        nonlocal buf, cnt
        need(k)
        v = buf & ((1 << k) - 1)
        buf >>= k
        cnt -= k
        return v

    def decode(t):
        nonlocal buf, cnt
        table, maxlen = t
        need(maxlen)
        e = table[buf & ((1 << maxlen) - 1)]
        length = e & 15
        buf >>= length
        cnt -= length
        return e >> 4

    lit = bits(8)
    if lit > 1:
        raise BlastError('bad literal flag %d' % lit)
    dict_bits = bits(8)
    if not 4 <= dict_bits <= 6:
        raise BlastError('bad dictionary size %d' % dict_bits)
    limit = raw_size if raw_size is not None else 1 << 62
    while len(out) < limit:
        if pos > n + 4:
            raise BlastError('input exhausted')
        if bits(1):
            sym = decode(_LEN)
            length = _BASE[sym] + bits(_EXTRA[sym])
            if length == 519:
                break
            shift = 2 if length == 2 else dict_bits
            dist = (decode(_DIST) << shift) + bits(shift) + 1
            if dist > len(out):
                raise BlastError('distance too far back')
            start = len(out) - dist
            if dist >= length:
                out += out[start:start + length]
            else:
                for i in range(length):
                    out.append(out[start + i])
        else:
            out.append(decode(_LIT) if lit else bits(8))
    return bytes(out)
